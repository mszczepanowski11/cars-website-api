namespace CarsWebsite;

public class Invoice
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public string InvoiceNumber { get; set; } = string.Empty;
    public int Month { get; set; }
    public int Year { get; set; }

    public decimal TotalAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal VatRate { get; set; } = 0.23m;

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }

    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}