namespace cars_website_api.CarsWebsite.DTOs.User;

public class PublicUserProfileDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? About { get; set; }
    public string? AccountType { get; set; }
    public string? CompanyName { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool EmailVerified { get; set; }
    public string? PhoneNumber { get; set; }
}
