namespace cars_website_api.CarsWebsite.DTOs.Advert;

public class FeatureDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public FeatureCategoryDto Category { get; set; }
}