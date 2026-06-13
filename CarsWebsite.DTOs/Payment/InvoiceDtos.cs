using CarsWebsite;
using cars_website_api.CarsWebsite.DTOs.Payment;

namespace cars_website_api.CarsWebsite.DTOs.Invoice;

public class InvoiceResponseDto
{
    public int Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal VatRate { get; set; }
    public InvoiceStatus Status { get; set; }
    public DateTime GeneratedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public List<PaymentResponseDto> Items { get; set; } = new();
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string? Nip { get; set; }
}
