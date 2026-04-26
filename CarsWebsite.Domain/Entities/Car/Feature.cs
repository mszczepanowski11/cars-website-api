using CarsWebsite;

namespace cars_website_api.CarsWebsite.Domain.Entities;

public class Feature
{
    public int Id { get; set; }
    public string Name { get; set; }
    
    public int CategoryId { get; set; }
    public FeatureCategory Category { get; set; }
    
    public ICollection<AdvertFeature> AdvertFeatures { get; set; }
    
}