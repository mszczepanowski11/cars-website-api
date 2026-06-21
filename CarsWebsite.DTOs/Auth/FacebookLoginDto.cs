using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs;

public class FacebookLoginDto
{
    [Required]
    public string AccessToken { get; set; } = string.Empty;
}
