using cars_website_api.CarsWebsite.Domain.Entities;

namespace cars_website_api.CarsWebsite.Domain.Entities;

public class FeatureCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    // Required — every FeatureCategory must belong to exactly one vehicle category. BrandId/ModelId
    // stay nullable: those two dimensions are meant to be wildcards ("applies to every brand/model
    // within the category"), but the category itself must never be a wildcard, since that was the
    // root cause of equipment leaking across unrelated vehicle categories (see Program.cs history).
    public int VehicleCategoryId { get; set; }
    public VehicleCategory VehicleCategory { get; set; } = null!;
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }
    public int? ModelId { get; set; }
    public Model? Model { get; set; }
    public ICollection<Feature> Features { get; set; } = new List<Feature>();
}
