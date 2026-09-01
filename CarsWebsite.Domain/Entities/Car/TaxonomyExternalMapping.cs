namespace cars_website_api.CarsWebsite.Domain.Entities;

// Taksonomia Etap 4: maps our taxonomy rows onto identifiers from an external catalogue
// (Akol and any future data provider).
//
// Why this exists: every import today resolves a brand/model by NAME - PartnerImportService
// normalizes the string and looks it up. That is exactly the mechanism that produced 863 excess
// model rows (54% of the table), because "Škoda"/"Skoda", " Golf"/"Golf" and re-runs of the same
// feed all look like new records. Once a supplier's own id is stored alongside our row, a
// re-import matches on that id first and cannot fork the taxonomy no matter how the name is
// spelled or re-spelled upstream.
//
// Kept as a separate table rather than columns on each entity so it stays additive (nothing about
// the existing taxonomy tables changes) and so one row can be mapped by several sources at once.
public class TaxonomyExternalMapping
{
    public int Id { get; set; }

    // Which external catalogue this identifier comes from, e.g. "akol". Lowercase by convention.
    public string SourceSystem { get; set; } = string.Empty;

    // Which taxonomy table InternalId points at: brand | model | generation | trim | engine |
    // category. Stored as a string rather than an enum so a new entity type never needs a
    // migration.
    public string EntityType { get; set; } = string.Empty;

    // The identifier as the external system knows it.
    public string ExternalId { get; set; } = string.Empty;

    // Our row id in the table named by EntityType.
    public int InternalId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
