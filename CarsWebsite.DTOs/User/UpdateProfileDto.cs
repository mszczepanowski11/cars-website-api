namespace cars_website_api.CarsWebsite.DTOs.User;

public class UpdateProfileDto
{
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? Street { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string? About { get; set; }
    public string? CompanyName { get; set; }
    public string? Nip { get; set; }
}
