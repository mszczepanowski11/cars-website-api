using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.Domain.Entities;

public class Brand
{
    public int Id { get; set; }
    [MaxLength(100)] public string Name { get; set; }
    [MaxLength(100)] public string Slug { get; set; }

    // Brand-level metadata (not per-advert): backs the "Samochody amerykańskie/japońskie/
    // chińskie" and "Samochody luksusowe" filters on Auta osobowe without duplicating a value
    // onto every single advert row.
    [MaxLength(50)] public string? OriginCountry { get; set; }
    public bool IsLuxury { get; set; }

    public ICollection<Model> Models { get; set; }
    public ICollection<VehicleCategory> Categories { get; set; }
}