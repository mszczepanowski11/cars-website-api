using System.ComponentModel.DataAnnotations;
using CarsWebsite;

namespace cars_website_api.CarsWebsite.DTOs;

public class RegisterDto
{
    [Required] public string Name { get; set; } = string.Empty;
    [Required] public string Surname { get; set; } = string.Empty;
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    [Required] public string Password { get; set; } = string.Empty;
    [Required] public DateOnly DateOfBirth { get; set; }
    public AccountType AccountType { get; set; } = AccountType.Personal;
    public BusinessType? BusinessType { get; set; }
    public string? CompanyName { get; set; }
    public string? Nip { get; set; }
}
