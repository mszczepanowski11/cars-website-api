using cars_website_api.CarsWebsite.Domain.Entities;

namespace CarsWebsite;

public class FavoriteAdvert
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int AdvertId { get; set; }
    public CarAdvert Advert { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}