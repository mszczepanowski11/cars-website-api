using cars_website_api.CarsWebsite.Enums;
using Microsoft.AspNetCore.StaticFiles.Infrastructure;

namespace CarsWebsite;

public class Advert
{
    public int Id { get; set; }
    public AdvertType AdvertType { get; set; }
    public string Title { get; set; }
    public decimal Price { get; set; }
    public string Description { get; set; }
    public string Location { get; set; }
    public List<string> Images { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    
    public int UserId { get; set; }  
    public User createdBy { get; set; }
    
    public VehicleDetails? VehicleDetails { get; set; }
    public PartDetails? PartDetails { get; set; }
}