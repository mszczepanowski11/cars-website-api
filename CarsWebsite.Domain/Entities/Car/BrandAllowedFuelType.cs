namespace cars_website_api.CarsWebsite.Domain.Entities;

// Fail-open allowlist: a brand with ZERO rows here has no fuel-type restriction at all. Only
// brands with at least one row are restricted to exactly the fuel types listed — this lets the
// feature ship with a handful of obvious exotic-brand rules without needing every brand seeded
// first, and guarantees an unseeded brand is never wrongly rejected.
public class BrandAllowedFuelType
{
    public int Id { get; set; }
    public int BrandId { get; set; }
    public Brand Brand { get; set; } = null!;
    public int FuelTypeId { get; set; }
    public FuelType FuelType { get; set; } = null!;
}
