namespace cars_website_api.CarsWebsite.Interfaces;

// Taksonomia Etap 4: resolve/record the link between an external catalogue's identifier and our
// taxonomy row, so a re-import matches on that identifier instead of on a name string.
public interface ITaxonomyMappingService
{
    // Known EntityType values. Strings, not an enum, so a new taxonomy table never needs a
    // migration to become mappable.
    public const string Brand = "brand";
    public const string Model = "model";
    public const string Generation = "generation";
    public const string Trim = "trim";
    public const string Engine = "engine";
    public const string Category = "category";

    // Our row id for this external identifier, or null when the source has never been mapped.
    Task<int?> ResolveAsync(string sourceSystem, string entityType, string externalId, CancellationToken ct = default);

    // Records the link. Idempotent: re-recording the same pair is a no-op, and a pair that already
    // points somewhere else is left untouched and reported, never silently repointed.
    Task RecordAsync(string sourceSystem, string entityType, string externalId, int internalId, CancellationToken ct = default);
}
