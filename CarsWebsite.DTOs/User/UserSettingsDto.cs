namespace cars_website_api.CarsWebsite.DTOs.User;

public class UserSettingsDto
{
    public bool EmailNotifications { get; set; }
    public bool PriceChangeAlerts { get; set; }
    public bool NewMessageAlerts { get; set; }
    public bool NewsletterSubscribed { get; set; }
}
