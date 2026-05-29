namespace cars_website_api.CarsWebsite.DTOs.Message;

public class StartConversationDto
{
    public int AdvertId { get; set; }
    public string InitialMessage { get; set; } = string.Empty;
}