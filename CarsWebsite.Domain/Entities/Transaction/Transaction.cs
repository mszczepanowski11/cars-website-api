using cars_website_api.CarsWebsite.Domain.Entities;

namespace CarsWebsite;

public enum TransactionType
{
    Reservation,
    Viewing,
    Purchase,
}

public enum TransactionStatus
{
    Pending,
    Confirmed,
    Cancelled,
    Completed,
}

public class Transaction
{
    public int Id { get; set; }
    public TransactionType Type { get; set; }
    public TransactionStatus Status { get; set; } = TransactionStatus.Pending;

    public int AdvertId { get; set; }
    public CarAdvert Advert { get; set; } = null!;

    public int BuyerId { get; set; }
    public User Buyer { get; set; } = null!;

    public int SellerId { get; set; }
    public User Seller { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ScheduledAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Notes { get; set; }
}
