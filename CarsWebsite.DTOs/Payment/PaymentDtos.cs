using CarsWebsite;

namespace cars_website_api.CarsWebsite.DTOs.Payment;

public class InitiatePaymentDto
{
    public ServiceType ServiceType { get; set; }
    public int? AdvertId { get; set; }
    public int DurationDays { get; set; } = 7;
}

public class PaymentInitiatedDto
{
    public int PaymentId { get; set; }
    public string PaymentUrl { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string OrderId { get; set; } = string.Empty;
}

public class PaymentResponseDto
{
    public int Id { get; set; }
    public ServiceType ServiceType { get; set; }
    public string ServiceDescription { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PLN";
    public PaymentStatus Status { get; set; }
    public string? ImojeTransactionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public int? AdvertId { get; set; }
    public int? DurationDays { get; set; }
}

public class ServicePriceDto
{
    public ServiceType ServiceType { get; set; }
    public int DurationDays { get; set; }
    public decimal Price { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class ImojeWebhookDto
{
    public string OrderId { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PLN";
}
