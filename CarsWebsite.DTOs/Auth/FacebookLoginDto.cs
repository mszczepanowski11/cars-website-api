using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs;

public class FacebookLoginDto
{
    [Required]
    public string AccessToken { get; set; } = string.Empty;

    // True only after the user has explicitly confirmed the "we'll create a CARIZO account from
    // your Facebook name/email" consent screen - a brand-new account is never created without it.
    public bool ConsentGiven { get; set; } = false;
}
