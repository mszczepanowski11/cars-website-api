using System.ComponentModel.DataAnnotations;

namespace CarsWebsite;

// CTO audit Etap 2: the taxonomy half of the schema adapter layer. A partner's raw field VALUES
// rarely match CARIZO's own vocabulary exactly (e.g. AKOL's category "Samochody osobowe" vs.
// CARIZO's slug "auta-osobowe", or "diesel"/"ropa" vs. CARIZO's FuelType "Diesel"). Brand/FuelType/
// Gearbox already tolerate a mismatch by auto-creating a new row (see PartnerImportService's
// GetOrCreate* methods) - workable but noisy (synonyms become duplicate rows an admin must merge
// later). Category has NO such fallback: an unmatched slug fails the whole item outright, since
// auto-creating categories would mean an integrator's typo silently adds a bogus top-level category
// to the whole marketplace. One row per (PartnerId, Field, ExternalValue) translates the partner's
// raw text to CARIZO's canonical value before the existing matching logic runs; an unmapped value
// falls through to that existing logic unchanged (exact-name auto-create for Brand/FuelType/Gearbox,
// still a hard failure for Category, precisely the case this mapping exists to prevent).
public class PartnerValueMapping
{
    public int Id { get; set; }

    public int PartnerId { get; set; }
    public Partner Partner { get; set; } = null!;

    // One of FieldNames below.
    [MaxLength(20)] public string Field { get; set; } = string.Empty;

    // Matched case-insensitively, trimmed - same normalization PartnerImportService already
    // applies to every taxonomy lookup.
    [MaxLength(200)] public string ExternalValue { get; set; } = string.Empty;

    // For Field="Category": CARIZO's VehicleCategory.Slug. For Brand/FuelType/Gearbox: CARIZO's
    // canonical Name (so mapping just prevents a duplicate synonym row rather than changing how
    // the lookup itself works). For Condition: literally "new" or "used".
    [MaxLength(200)] public string InternalValue { get; set; } = string.Empty;

    public static readonly string[] FieldNames = { "Category", "Brand", "FuelType", "Gearbox", "Condition" };
}
