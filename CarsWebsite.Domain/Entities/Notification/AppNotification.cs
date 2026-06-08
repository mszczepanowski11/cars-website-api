namespace CarsWebsite;

public class AppNotification
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public EmailNotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int? AdvertId { get; set; }
    public int? PaymentId { get; set; }
    public int? InvoiceId { get; set; }
    public bool EmailSent { get; set; }
}
