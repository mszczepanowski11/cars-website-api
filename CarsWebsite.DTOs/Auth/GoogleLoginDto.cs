using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs;

public class GoogleLoginDto
{
    [Required]
    public string Credential { get; set; } = string.Empty;
}
