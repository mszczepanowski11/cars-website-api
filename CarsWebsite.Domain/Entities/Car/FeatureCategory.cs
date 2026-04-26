namespace cars_website_api.CarsWebsite.Domain.Entities;

public class FeatureCategory
{
    public int Id { get; set; }
    public string Name { get; set; }

    public ICollection<Feature> Features { get; set; }
}