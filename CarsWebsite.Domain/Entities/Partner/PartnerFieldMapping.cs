using System.ComponentModel.DataAnnotations;

namespace CarsWebsite;

// CTO audit Etap 2 (🔴 "warstwa adaptera schematu"): PartnerImportService's ParseXml/ParseCsv used
// to hardcode CARIZO's own field names (XML element / CSV column) as the ONLY accepted schema, so
// onboarding a real integrator (AKOL, 44FOX - each with its own existing feed format) meant writing
// new parsing code, not just configuring a new partner. One row per remapped field: OurField is one
// of the fixed names PartnerImportService knows internally (see FieldNames below); SourcePath is
// the partner's own element/column name for that field. A partner with no rows for a given OurField
// falls back to CARIZO's own default name - fully backward compatible with partners (like the
// existing "Dla firm" self-service signups) whose feed already matches CARIZO's schema.
public class PartnerFieldMapping
{
    public int Id { get; set; }

    public int PartnerId { get; set; }
    public Partner Partner { get; set; } = null!;

    [MaxLength(50)] public string OurField { get; set; } = string.Empty;
    [MaxLength(200)] public string SourcePath { get; set; } = string.Empty;

    // The exact set PartnerImportService's ParseXml/ParseCsv understand - kept here (not just in
    // the service) so the admin CRUD endpoint can validate OurField without a service reference.
    // Deliberately excludes the image list: XML represents it as a container element with repeated
    // child items and CSV as a single pipe-delimited column, two different shapes that don't fit
    // this flat "one field = one source name" model - still fixed to CARIZO's own Images/Image
    // (XML) and ImageUrls (CSV) names for v1, a known scope limit on top of this adapter layer.
    public static readonly string[] FieldNames =
    {
        "ExternalId", "Title", "Description", "Price", "Category", "VehicleSubtype",
        "Brand", "Model", "Year", "Mileage", "FuelType", "Gearbox", "PowerHP", "Vin",
        "City", "Region", "Condition", "PartCategory", "PartSubcategory", "CatalogNumber",
        "OemNumber", "PartManufacturer", "Compatibility", "AxleCount", "Payload",
        "CargoLength", "CargoHeight", "Volume", "OperatingWeightKg", "WorkingWidthCm",
        "MaxDiggingDepthM", "BucketCapacityL", "TankCapacityL",
    };
}
