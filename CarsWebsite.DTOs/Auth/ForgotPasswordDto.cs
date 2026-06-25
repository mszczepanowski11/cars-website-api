using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs;

public class ForgotPasswordDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}
