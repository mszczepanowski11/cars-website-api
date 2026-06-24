using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs.Message;

public class StartConversationDto
{
    public int AdvertId { get; set; }
    [MaxLength(4000)] public string? InitialMessage { get; set; }
}
