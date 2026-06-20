using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs.Advert;

public class PromoteAdvertDto
{
    [Required]
    [RegularExpression("^(TOP|PREMIUM|FEATURED|REFRESH|EventFeatured)$",
        ErrorMessage = "Nieprawidłowy typ promocji.")]
    public string Type { get; set; } = string.Empty;
    public int DurationDays { get; set; }
}
