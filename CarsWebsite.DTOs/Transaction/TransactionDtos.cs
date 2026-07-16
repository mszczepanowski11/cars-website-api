using System.ComponentModel.DataAnnotations;
using CarsWebsite;

namespace cars_website_api.CarsWebsite.DTOs.Transaction;

public class TransactionResponseDto
{
    public int Id { get; set; }
    public TransactionType Type { get; set; }
    public TransactionStatus Status { get; set; }
    public int AdvertId { get; set; }
    public string AdvertTitle { get; set; } = string.Empty;
    public decimal AdvertPrice { get; set; }
    public int BuyerId { get; set; }
    public string BuyerName { get; set; } = string.Empty;
    public int SellerId { get; set; }
    public string SellerName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Notes { get; set; }
    public string? SellerPhone { get; set; }
}

public class CreateTransactionDto
{
    [Required]
    public TransactionType Type { get; set; }

    [Required]
    public int AdvertId { get; set; }

    public DateTime? ScheduledAt { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }
}

public class UpdateTransactionStatusDto
{
    [Required]
    public TransactionStatus Status { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }
}
