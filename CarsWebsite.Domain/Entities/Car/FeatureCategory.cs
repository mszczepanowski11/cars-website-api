using cars_website_api.CarsWebsite.Domain.Entities;

namespace cars_website_api.CarsWebsite.Domain.Entities;

public class FeatureCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? VehicleCategoryId { get; set; }
    public VehicleCategory? VehicleCategory { get; set; }
    public ICollection<Feature> Features { get; set; } = new List<Feature>();
}
