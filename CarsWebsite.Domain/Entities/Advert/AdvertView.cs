namespace cars_website_api.CarsWebsite.Domain.Entities;

public class AdvertView
{
    public int Id { get; set; }
    public int AdvertId { get; set; }
    public int? UserId { get; set; }
    public string? IpAddress { get; set; }
    public DateTime ViewedAt { get; set; } = DateTime.UtcNow;
}
