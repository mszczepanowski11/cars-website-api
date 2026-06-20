using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs.Advert;

public class PromoteAdvertDto
{
    [Required]
    [RegularExpression("^(TOP|PREMIUM|FEATURED)$", ErrorMessage = "Type musi być TOP, PREMIUM lub FEATURED.")]
    public string Type { get; set; } = string.Empty;

    [Required] [Range(1, 365)] public int DurationDays { get; set; }
}
