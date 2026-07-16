namespace CarsWebsite;

public class Payment
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int? AdvertId { get; set; }
    public Advert? Advert { get; set; }

    public int? EventId { get; set; }
    public Event? Event { get; set; }

    public ServiceType ServiceType { get; set; }
    public string ServiceDescription { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PLN";

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    public string? ImojeTransactionId { get; set; }
    public string? ImojeOrderId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }

    public int? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public int? DurationDays { get; set; }

    // Only set for ServiceType.Subscription payments; DurationDays stays a genuine day count for
    // those too (used for e.g. invoice display) instead of being overloaded to store the tier.
    public SubscriptionTier? SubscriptionTier { get; set; }

    // Billing snapshot for invoice generation
    public string? BillingName { get; set; }
    public string? BillingNip { get; set; }
    public string? BillingStreet { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCity { get; set; }
}
