namespace cars_website_api.CarsWebsite.Domain.Entities;

public class PartCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NamePl { get; set; }
    public int SortOrder { get; set; }

    public ICollection<PartSubcategory> Subcategories { get; set; } = new List<PartSubcategory>();
}
