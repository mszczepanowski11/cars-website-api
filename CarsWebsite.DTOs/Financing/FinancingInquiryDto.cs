using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs.Financing;

public class CreateFinancingInquiryDto
{
    [Required]
    public int AdvertId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = "";

    [Required]
    [MaxLength(30)]
    public string Phone { get; set; } = "";

    [EmailAddress]
    [MaxLength(200)]
    public string? Email { get; set; }

    [Required]
    public string Type { get; set; } = "leasing";

    public decimal? Price { get; set; }

    [Range(0, 100)]
    public int? DownPaymentPct { get; set; }

    [Range(1, 240)]
    public int? Months { get; set; }
}
