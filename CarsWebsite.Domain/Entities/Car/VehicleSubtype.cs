namespace cars_website_api.CarsWebsite.Domain.Entities;

public class VehicleSubtype
{
    public int Id { get; set; }
    public int VehicleCategoryId { get; set; }
    public VehicleCategory VehicleCategory { get; set; } = null!;
    public string Name { get; set; } = string.Empty; // e.g. "Ciągnik siodłowy", "Wywrotka"
    public string? NamePl { get; set; }
    public string? Slug { get; set; }
    public int SortOrder { get; set; }
}
