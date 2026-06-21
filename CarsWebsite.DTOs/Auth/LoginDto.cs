using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs;

public class LoginDto
{
    [Required(ErrorMessage = "Email jest wymagany.")]
    [EmailAddress(ErrorMessage = "Nieprawidłowy format email.")]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Hasło jest wymagane.")]
    [MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    public string? TurnstileToken { get; set; }
}
