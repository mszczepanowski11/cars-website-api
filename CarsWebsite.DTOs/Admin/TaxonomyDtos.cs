namespace cars_website_api.CarsWebsite.DTOs.Admin;

public class CreateFeatureDto
{
    public string Name { get; set; } = string.Empty;
    public int CategoryId { get; set; }
}

public class CreateFeatureCategoryDto
{
    public string Name { get; set; } = string.Empty;
    public int? VehicleCategoryId { get; set; }
}
