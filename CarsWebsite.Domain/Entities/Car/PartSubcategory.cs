namespace cars_website_api.CarsWebsite.Domain.Entities;

public class PartSubcategory
{
    public int Id { get; set; }
    public int PartCategoryId { get; set; }
    public PartCategory PartCategory { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string? NamePl { get; set; }
    public int SortOrder { get; set; }
}
