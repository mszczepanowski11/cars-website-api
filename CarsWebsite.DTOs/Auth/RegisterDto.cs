using CarsWebsite;

namespace cars_website_api.CarsWebsite.DTOs;

public class RegisterDto
{
    public string Name { get; set; }
    public string Surname { get; set; }
    public string Email { get; set; }
    public string PhoneNumber { get; set; }
    public string Password { get; set; }
    public AccountType AccountType { get; set; } = AccountType.Personal;
    public string? CompanyName { get; set; }
    public string? Nip { get; set; }
}