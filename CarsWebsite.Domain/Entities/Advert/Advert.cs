using Microsoft.AspNetCore.StaticFiles.Infrastructure;

namespace CarsWebsite;

public class Advert
{
    public int Id { get; set; }

    public string Title { get; set; }
    public string Description { get; set; }

    public decimal Price { get; set; }
    public string Currency { get; set; } = "PLN";

    public string? City { get; set; }
    public string? Region { get; set; }

    public int UserId { get; set; }
    public User createdBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<AdvertImage> Images { get; set; }
    
}