namespace CarsWebsite;

public class FinancingInquiry
{
    public int Id { get; set; }
    public int AdvertId { get; set; }
    public int? UserId { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Email { get; set; }
    public string Type { get; set; } = "leasing";      // "leasing" | "credit"
    public decimal? Price { get; set; }
    public int? DownPaymentPct { get; set; }
    public int? Months { get; set; }
    public string Status { get; set; } = "new";         // "new" | "contacted" | "closed"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
