using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs;

public class LoginDto
{
    [Required] [EmailAddress] [MaxLength(256)] public string Email { get; set; }
    [Required] [MaxLength(128)] public string Password { get; set; }
}
