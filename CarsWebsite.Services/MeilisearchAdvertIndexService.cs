using System.Net.Http.Headers;
using System.Net.Http.Json;
using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.DTOs.Search;
using CarsWebsite;
using Meilisearch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace cars_website_api.CarsWebsite.Services;

// Host+ApiKey alongside the constructed MeilisearchClient - the Meilisearch .NET client (0.20.0)
// has no method for the /experimental-features endpoint ReindexAllAsync needs (see below), so this
// service makes that one call directly over HTTP using the same connection details Program.cs used
// to build the client. Host is empty when Meilisearch is disabled, mirroring MeilisearchClient? being
// null in that case.
public sealed record MeilisearchConnectionOptions(string Host, string ApiKey);

// See docs/search-engine-evaluation.md for why Meilisearch was picked over OpenSearch for this
// project's scale, and the plan this class implements: hook indexing into AdvertService's write
// path, use Meilisearch for both free-text match (typo tolerance, relevance ranking - exactly what
// plain MySQL FULLTEXT lacks) and, since the Etap 4 attribute-filter pass, structured facet filters
// (AttributeFilterDto) too, failing open to the existing MySQL FULLTEXT/EF `.Where()` query
// whenever Meilisearch is unset, unreachable, or erroring.
public class MeilisearchAdvertIndexService : IAdvertSearchIndexService
{
    private const string IndexUid = "adverts";
    private readonly MeilisearchClient? _client;
    private readonly MeilisearchConnectionOptions _connection;
    private readonly AppDbContext _context;
    private readonly ILogger<MeilisearchAdvertIndexService> _logger;

    public bool IsEnabled => _client != null;

    public MeilisearchAdvertIndexService(AppDbContext context, ILogger<MeilisearchAdvertIndexService> logger, MeilisearchClient? client, MeilisearchConnectionOptions connection)
    {
        _context = context;
        _logger = logger;
        _client = client;
        _connection = connection;
    }

    // AttributeDefinitionId -> attr_{id} field name, matching MeilisearchAttributeFilterBuilder.
    private static string AttributeFieldName(int attributeDefinitionId) => $"attr_{attributeDefinitionId}";

    // Exactly one of ValueText/ValueNumber/ValueBool/ValueDate is populated per AdvertAttributeValue
    // row (see that entity's doc comment) - each becomes one attr_{id} field of the matching JSON
    // type, so Meilisearch's numeric/boolean filter operators (>=, <=, =) work directly on it instead
    // of everything being a string.
    private static Dictionary<string, object> ToAttributeFields(IEnumerable<AdvertAttributeValue> values)
    {
        var fields = new Dictionary<string, object>();
        foreach (var v in values)
        {
            if (v.ValueBool.HasValue) fields[AttributeFieldName(v.AttributeDefinitionId)] = v.ValueBool.Value;
            else if (v.ValueNumber.HasValue) fields[AttributeFieldName(v.AttributeDefinitionId)] = v.ValueNumber.Value;
            else if (v.ValueText != null) fields[AttributeFieldName(v.AttributeDefinitionId)] = v.ValueText;
            else if (v.ValueDate.HasValue) fields[AttributeFieldName(v.AttributeDefinitionId)] = v.ValueDate.Value.ToString("O");
        }
        return fields;
    }

    private static AdvertSearchDocument ToDocument(CarAdvert advert, IEnumerable<AdvertAttributeValue> attributeValues) => new()
    {
        Id = advert.Id,
        Title = advert.Title,
        Description = advert.Description,
        CategoryId = advert.VehicleCategoryId,
        BrandId = advert.BrandId,
        ModelId = advert.ModelId,
        Price = advert.Price,
        Year = advert.Year,
        CreatedAt = advert.CreatedAt,
        Attributes = ToAttributeFields(attributeValues),
    };

    // Whether `advert` should currently be text-searchable - mirrors the gating predicate
    // AdvertService.SearchCarAdvertsAsync applies (IsActive && !IsHidden && not expired). Adverts
    // that don't meet this get removed from the index rather than indexed, so the index never needs
    // its own copy of that filter at query time.
    private static bool IsSearchable(CarAdvert advert) =>
        advert.IsActive && !advert.IsHidden && (advert.ExpiresAt == null || advert.ExpiresAt > DateTime.UtcNow);

    public async Task IndexAsync(CarAdvert advert, CancellationToken cancellationToken = default)
    {
        if (_client == null) return;
        try
        {
            if (!IsSearchable(advert))
            {
                await DeleteAsync(advert.Id, cancellationToken);
                return;
            }
            var attributeValues = await _context.AdvertAttributeValues
                .AsNoTracking()
                .Where(v => v.AdvertId == advert.Id)
                .ToListAsync(cancellationToken);
            var index = _client.Index(IndexUid);
            await index.AddDocumentsAsync(new[] { ToDocument(advert, attributeValues) }, "id", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Meilisearch] IndexAsync failed for advert {AdvertId} - search will fall back to MySQL for this advert until the next successful sync", advert.Id);
        }
    }

    public async Task DeleteAsync(int advertId, CancellationToken cancellationToken = default)
    {
        if (_client == null) return;
        try
        {
            var index = _client.Index(IndexUid);
            await index.DeleteOneDocumentAsync(advertId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Meilisearch] DeleteAsync failed for advert {AdvertId}", advertId);
        }
    }

    public async Task<List<int>?> SearchIdsAsync(string? text, string? filter, int limit, CancellationToken cancellationToken = default)
    {
        if (_client == null) return null;
        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(filter)) return null;
        try
        {
            var index = _client.Index(IndexUid);
            var query = new SearchQuery { Limit = limit, AttributesToRetrieve = new[] { "id" } };
            if (!string.IsNullOrWhiteSpace(filter))
                query.Filter = filter;
            var result = await index.SearchAsync<AdvertSearchDocument>(text ?? string.Empty, query, cancellationToken);
            return result.Hits.Select(h => h.Id).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Meilisearch] SearchIdsAsync failed for query {Query} filter {Filter} - falling back to MySQL", text, filter);
            return null;
        }
    }

    // The `CONTAINS` filter operator (used for MultiSelect/Select attribute text matching - see
    // MeilisearchAttributeFilterBuilder) sits behind Meilisearch's `containsFilter` experimental
    // feature flag. Enabling it is a one-time, idempotent PATCH against an endpoint the Meilisearch
    // .NET client (0.20.0) doesn't wrap, so this makes the call directly. Best-effort: if the API key
    // in use isn't allowed to toggle experimental features (some managed hosts reserve that to the
    // instance owner), log and continue - SearchIdsAsync already fails open on any filter error, so
    // attribute-text filters just keep using the MySQL fallback until this succeeds.
    private async Task EnableContainsFilterAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_connection.Host)) return;
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(_connection.Host) };
            if (!string.IsNullOrEmpty(_connection.ApiKey))
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _connection.ApiKey);
            using var response = await http.PatchAsJsonAsync("/experimental-features", new { containsFilter = true }, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Meilisearch] Could not enable containsFilter experimental feature - attribute text filters (CONTAINS) will fail open to MySQL until this succeeds");
        }
    }

    public async Task<int> ReindexAllAsync(CancellationToken cancellationToken = default)
    {
        if (_client == null) return 0;

        await EnableContainsFilterAsync(cancellationToken);

        var index = _client.Index(IndexUid);

        // Every active AttributeDefinition gets its own attr_{id} filterable field - dynamic, so a
        // new attribute an admin adds through the category/attribute editor becomes filterable in
        // Meilisearch on the next reindex with no code change or index-schema migration.
        var attributeDefinitionIds = await _context.AttributeDefinitions
            .AsNoTracking()
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);
        var filterableAttributes = new List<string> { "categoryId", "brandId", "modelId" };
        filterableAttributes.AddRange(attributeDefinitionIds.Select(AttributeFieldName));

        // Idempotent settings push - safe to run on every reindex, not just once at index creation.
        await index.UpdateSearchableAttributesAsync(new[] { "title", "description" }, cancellationToken);
        await index.UpdateFilterableAttributesAsync(filterableAttributes, cancellationToken);
        await index.UpdateSortableAttributesAsync(new[] { "price", "year", "createdAt" }, cancellationToken);
        await index.DeleteAllDocumentsAsync(cancellationToken);

        var total = 0;
        const int batchSize = 1000;
        var query = _context.CarAdverts
            .AsNoTracking()
            .Where(a => a.IsActive && !a.IsHidden && (a.ExpiresAt == null || a.ExpiresAt > DateTime.UtcNow))
            .OrderBy(a => a.Id);

        List<CarAdvert> batch;
        var lastId = 0;
        do
        {
            batch = await query.Where(a => a.Id > lastId).Take(batchSize).ToListAsync(cancellationToken);
            if (batch.Count == 0) break;

            // Batch-load attribute values for the whole page in one query instead of one query per
            // advert - the same shape as every other N+1-avoidance pattern in this codebase.
            var batchIds = batch.Select(a => a.Id).ToList();
            var attributeValuesByAdvertId = (await _context.AdvertAttributeValues
                    .AsNoTracking()
                    .Where(v => batchIds.Contains(v.AdvertId))
                    .ToListAsync(cancellationToken))
                .GroupBy(v => v.AdvertId)
                .ToDictionary(g => g.Key, g => (IEnumerable<AdvertAttributeValue>)g);

            var documents = batch
                .Select(a => ToDocument(a, attributeValuesByAdvertId.TryGetValue(a.Id, out var vals) ? vals : Array.Empty<AdvertAttributeValue>()))
                .ToList();
            await index.AddDocumentsAsync(documents, "id", cancellationToken);
            total += batch.Count;
            lastId = batch[^1].Id;
        } while (batch.Count == batchSize);

        _logger.LogInformation("[Meilisearch] ReindexAllAsync indexed {Count} adverts", total);
        return total;
    }
}
