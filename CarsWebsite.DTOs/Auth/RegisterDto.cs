using System.ComponentModel.DataAnnotations;
using CarsWebsite;

namespace cars_website_api.CarsWebsite.DTOs;

public class RegisterDto
{
    [Required] [MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required] [MaxLength(100)] public string Surname { get; set; } = string.Empty;
    [Required] [EmailAddress] [MaxLength(256)] public string Email { get; set; } = string.Empty;
    // E.164: leading '+', country code digit 1-9, then 6-14 more digits (max 15 digits total).
    // Required because the User entity's PhoneNumber column is NOT NULL - registering without one
    // used to throw an unhandled DbUpdateException instead of a clean validation error.
    [Required]
    [RegularExpression(@"^\+[1-9]\d{6,14}$", ErrorMessage = "Numer telefonu musi być w formacie międzynarodowym, np. +48123456789.")]
    public string PhoneNumber { get; set; } = string.Empty;
    [Required] [MinLength(8)] [MaxLength(128)] public string Password { get; set; } = string.Empty;
    [Required] public DateOnly DateOfBirth { get; set; }
    public AccountType AccountType { get; set; } = AccountType.Personal;
    public BusinessType? BusinessType { get; set; }
    [MaxLength(200)] public string? CompanyName { get; set; }
    [MaxLength(10)] public string? Nip { get; set; }
}
