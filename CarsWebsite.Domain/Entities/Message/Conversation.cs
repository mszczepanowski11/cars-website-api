namespace CarsWebsite;

public class Conversation
{
    public int Id { get; set; }
    public int BuyerId { get; set; }
    public User Buyer { get; set; } = null!;
    public int SellerId { get; set; }
    public User Seller { get; set; } = null!;
    public int AdvertId { get; set; }
    public Advert Advert { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;
    public List<Message> Messages { get; set; } = new();
}