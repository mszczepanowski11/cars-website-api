namespace cars_website_api.CarsWebsite.DTOs.Category;

public class CategoryWithCountDto
{
    public int Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IconName { get; set; } = string.Empty;
    public int AdvertCount { get; set; }
}