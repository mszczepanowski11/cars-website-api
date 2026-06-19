using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs.Message;

public class SendMessageDto
{
    [Required] [MaxLength(4000)] public string Content { get; set; } = string.Empty;
}
