using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.DTOs.Search;
using CarsWebsite;
using Meilisearch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace cars_website_api.CarsWebsite.Services;

// See docs/search-engine-evaluation.md for why Meilisearch was picked over OpenSearch for this
// project's scale, and the sketched plan this class implements: hook indexing into AdvertService's
// write path, use Meilisearch only for the free-text match (typo tolerance, relevance ranking -
// exactly what plain MySQL FULLTEXT lacks), and fail open to the existing MySQL FULLTEXT query
// whenever Meilisearch is unset, unreachable, or erroring.
public class MeilisearchAdvertIndexService : IAdvertSearchIndexService
{
    private const string IndexUid = "adverts";
    private readonly MeilisearchClient? _client;
    private readonly AppDbContext _context;
    private readonly ILogger<MeilisearchAdvertIndexService> _logger;

    public bool IsEnabled => _client != null;

    public MeilisearchAdvertIndexService(AppDbContext context, ILogger<MeilisearchAdvertIndexService> logger, MeilisearchClient? client)
    {
        _context = context;
        _logger = logger;
        _client = client;
    }

    private static AdvertSearchDocument ToDocument(CarAdvert advert) => new()
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
            var index = _client.Index(IndexUid);
            await index.AddDocumentsAsync(new[] { ToDocument(advert) }, "id", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Meilisearch] IndexAsync failed for advert {AdvertId} - search will fall back to MySQL FULLTEXT for this advert until the next successful sync", advert.Id);
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

    public async Task<List<int>?> SearchIdsAsync(string text, int limit, CancellationToken cancellationToken = default)
    {
        if (_client == null || string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var index = _client.Index(IndexUid);
            var query = new SearchQuery { Limit = limit, AttributesToRetrieve = new[] { "id" } };
            var result = await index.SearchAsync<AdvertSearchDocument>(text, query, cancellationToken);
            return result.Hits.Select(h => h.Id).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Meilisearch] SearchIdsAsync failed for query {Query} - falling back to MySQL FULLTEXT", text);
            return null;
        }
    }

    public async Task<int> ReindexAllAsync(CancellationToken cancellationToken = default)
    {
        if (_client == null) return 0;

        var index = _client.Index(IndexUid);
        // Idempotent settings push - safe to run on every reindex, not just once at index creation.
        await index.UpdateSearchableAttributesAsync(new[] { "title", "description" }, cancellationToken);
        await index.UpdateFilterableAttributesAsync(new[] { "categoryId", "brandId", "modelId" }, cancellationToken);
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
            var documents = batch.Select(ToDocument).ToList();
            await index.AddDocumentsAsync(documents, "id", cancellationToken);
            total += batch.Count;
            lastId = batch[^1].Id;
        } while (batch.Count == batchSize);

        _logger.LogInformation("[Meilisearch] ReindexAllAsync indexed {Count} adverts", total);
        return total;
    }
}
