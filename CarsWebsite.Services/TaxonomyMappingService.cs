using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Services;

public class TaxonomyMappingService : ITaxonomyMappingService
{
    private readonly AppDbContext _context;
    private readonly ILogger<TaxonomyMappingService> _logger;

    public TaxonomyMappingService(AppDbContext context, ILogger<TaxonomyMappingService> logger)
    {
        _context = context;
        _logger = logger;
    }

    // External systems are inconsistent about casing and padding in their identifiers; normalizing
    // here means "AK-1234", "ak-1234" and " AK-1234 " can never become three separate mappings.
    private static string Norm(string s) => (s ?? string.Empty).Trim().ToLowerInvariant();

    public async Task<int?> ResolveAsync(string sourceSystem, string entityType, string externalId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(externalId)) return null;

        var row = await _context.TaxonomyExternalMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(m =>
                m.SourceSystem == Norm(sourceSystem) &&
                m.EntityType == Norm(entityType) &&
                m.ExternalId == Norm(externalId), ct);

        return row?.InternalId;
    }

    public async Task RecordAsync(string sourceSystem, string entityType, string externalId, int internalId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(externalId) || internalId <= 0) return;

        var source = Norm(sourceSystem);
        var type = Norm(entityType);
        var ext = Norm(externalId);

        var existing = await _context.TaxonomyExternalMappings
            .FirstOrDefaultAsync(m => m.SourceSystem == source && m.EntityType == type && m.ExternalId == ext, ct);

        if (existing is not null)
        {
            // Never silently repoint: if the supplier now claims this id is a different row, that
            // is a real data conflict a human needs to look at, not something to overwrite.
            if (existing.InternalId != internalId)
            {
                _logger.LogWarning(
                    "[TaxonomyMapping] {Source}/{Type}/{External} already maps to {Existing}, refusing to repoint to {New}",
                    source, type, ext, existing.InternalId, internalId);
            }
            return;
        }

        _context.TaxonomyExternalMappings.Add(new TaxonomyExternalMapping
        {
            SourceSystem = source,
            EntityType = type,
            ExternalId = ext,
            InternalId = internalId,
            CreatedAt = DateTime.UtcNow,
        });

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two concurrent imports can race on the unique key; the winner's mapping is just as
            // correct as ours, so drop this one instead of failing the whole import item.
            _context.ChangeTracker.Clear();
            _logger.LogDebug("[TaxonomyMapping] Concurrent insert for {Source}/{Type}/{External}, keeping the existing row",
                source, type, ext);
        }
    }
}
