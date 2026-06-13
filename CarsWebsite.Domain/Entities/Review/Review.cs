namespace cars_website_api.CarsWebsite.Domain.Entities;

public class Review
{
    public int Id { get; set; }
    public int SellerId { get; set; }
    public int BuyerId { get; set; }
    public int AdvertId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
