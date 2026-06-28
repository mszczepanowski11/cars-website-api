using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.Domain.Entities;

public class VehicleCategory
{
    public int Id { get; set; }
    [MaxLength(100)] public string Slug { get; set; } = string.Empty;
    [MaxLength(100)] public string Name { get; set; } = string.Empty;
    [MaxLength(255)] public string Description { get; set; } = string.Empty;
    [MaxLength(100)] public string IconName { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public ICollection<Brand> Brands { get; set; }
    public ICollection<VehicleSubtype> Subtypes { get; set; } = new List<VehicleSubtype>();
}