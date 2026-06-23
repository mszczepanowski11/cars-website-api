using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs.User;

public class UpdateProfileDto
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Surname { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? Region { get; set; }

    [MaxLength(200)]
    public string? Street { get; set; }

    [MaxLength(20)]
    public string? PostalCode { get; set; }

    [MaxLength(50)]
    public string? Country { get; set; }

    [MaxLength(2000)]
    public string? About { get; set; }

    [MaxLength(200)]
    public string? CompanyName { get; set; }

    [MaxLength(10)]
    public string? Nip { get; set; }
}
