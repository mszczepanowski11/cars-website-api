using cars_website_api.CarsWebsite.Domain.Entities;

namespace CarsWebsite;

public class AdvertFeature
{
    public int AdvertId { get; set; }
    
    public CarAdvert Advert { get; set; }
    public int FeatureId { get; set; }
    public Feature Feature { get; set; }
}