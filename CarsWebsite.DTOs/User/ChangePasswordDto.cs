using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs.User;

public class ChangePasswordDto
{
    [Required]
    [MaxLength(128)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    [MaxLength(128)]
    public string NewPassword { get; set; } = string.Empty;
}
