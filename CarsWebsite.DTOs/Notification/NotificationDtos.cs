using CarsWebsite;

namespace cars_website_api.CarsWebsite.DTOs.Notification;

public class NotificationResponseDto
{
    public int Id { get; set; }
    public EmailNotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? AdvertId { get; set; }
    public int? PaymentId { get; set; }
    public int? InvoiceId { get; set; }
}

public class NotificationPreferenceDto
{
    public string Category { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool EmailEnabled { get; set; }
}

public class UpdateNotificationPreferenceDto
{
    public string Category { get; set; } = string.Empty;
    public bool EmailEnabled { get; set; }
}
