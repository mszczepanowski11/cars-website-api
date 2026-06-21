using System.ComponentModel.DataAnnotations;
using CarsWebsite;

namespace cars_website_api.CarsWebsite.DTOs;

public class RegisterDto
{
    [Required] [MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required] [MaxLength(100)] public string Surname { get; set; } = string.Empty;
    [Required] [EmailAddress] [MaxLength(256)] public string Email { get; set; } = string.Empty;
    [MinLength(9)] [MaxLength(20)] public string? PhoneNumber { get; set; }
    [Required] [MinLength(8)] [MaxLength(128)] public string Password { get; set; } = string.Empty;
    [Required] public DateOnly DateOfBirth { get; set; }
    public AccountType AccountType { get; set; } = AccountType.Personal;
    public BusinessType? BusinessType { get; set; }
    [MaxLength(200)] public string? CompanyName { get; set; }
    [MaxLength(10)] public string? Nip { get; set; }
}
