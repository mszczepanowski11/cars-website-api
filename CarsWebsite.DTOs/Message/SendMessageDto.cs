using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs.Message;

public class SendMessageDto
{
    [Required]
    [MaxLength(5000, ErrorMessage = "Wiadomość nie może przekraczać 5000 znaków.")]
    public string Content { get; set; } = string.Empty;
}