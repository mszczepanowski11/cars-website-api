using CarsWebsite;

namespace cars_website_api.CarsWebsite.Interfaces;

public interface INotificationService
{
    Task NotifyAsync(
        int userId,
        EmailNotificationType type,
        string title,
        string content,
        int? advertId = null,
        int? paymentId = null,
        int? invoiceId = null);
}
