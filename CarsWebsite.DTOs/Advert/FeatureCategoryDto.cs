namespace cars_website_api.CarsWebsite.DTOs.Advert;

public class FeatureCategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? VehicleCategoryId { get; set; }
    public List<FeatureDto> Features { get; set; } = new();
}
