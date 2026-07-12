namespace cars_website_api.CarsWebsite.DTOs.Car;

public class BrandDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Slug { get; set; }
    public string? OriginCountry { get; set; }
    public bool IsLuxury { get; set; }
}