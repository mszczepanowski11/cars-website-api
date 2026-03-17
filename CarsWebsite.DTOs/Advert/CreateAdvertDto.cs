using cars_website_api.CarsWebsite.Enums;
using CarsWebsite;

namespace cars_website_api.CarsWebsite.DTOs.Advert;

public class CreateAdvertDto
{
    public AdvertType AdvertType { get; set; }
    public string Title { get; set; }
    public decimal Price { get; set; }
    public string Description { get; set; }
    public string Location { get; set; }
    public List<string>? Images { get; set; }
    
    public VehicleDetails? VehicleDetails { get; set; }
    public PartDetails? PartDetails { get; set; }
}