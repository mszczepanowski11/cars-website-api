using CarsWebsite;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.EntityFrameworkCore;

public class NotificationService : INotificationService
{
    private static readonly Dictionary<EmailNotificationType, string> Categories = new()
    {
        [EmailNotificationType.AccountCreated]       = "Registration",
        [EmailNotificationType.PasswordChanged]      = "Registration",
        [EmailNotificationType.PasswordReset]        = "Registration",

        [EmailNotificationType.AdvertAdded]          = "Adverts",
        [EmailNotificationType.AdvertPublished]      = "Adverts",
        [EmailNotificationType.AdvertRejected]       = "Adverts",
        [EmailNotificationType.AdvertDeleted]        = "Adverts",
        [EmailNotificationType.AdvertMarkedSold]     = "Adverts",

        [EmailNotificationType.AdvertExpiring7Days]  = "AdvertExpiry",
        [EmailNotificationType.AdvertExpiring3Days]  = "AdvertExpiry",
        [EmailNotificationType.AdvertExpiring1Day]   = "AdvertExpiry",
        [EmailNotificationType.AdvertExpired]        = "AdvertExpiry",

        [EmailNotificationType.PromotionPurchased]   = "Promotions",
        [EmailNotificationType.PromotionActivated]   = "Promotions",
        [EmailNotificationType.TopStarted]           = "Promotions",
        [EmailNotificationType.PremiumStarted]       = "Promotions",
        [EmailNotificationType.FeaturedStarted]      = "Promotions",
        [EmailNotificationType.RefreshStarted]       = "Promotions",

        [EmailNotificationType.PromotionExpiring3Days] = "PromotionExpiry",
        [EmailNotificationType.PromotionExpiring1Day]  = "PromotionExpiry",
        [EmailNotificationType.PromotionExpired]       = "PromotionExpiry",

        [EmailNotificationType.PaymentConfirmed]     = "Payments",
        [EmailNotificationType.PaymentFailed]        = "Payments",
        [EmailNotificationType.PaymentRefunded]      = "Payments",

        [EmailNotificationType.InvoiceGenerated]     = "Invoices",
        [EmailNotificationType.InvoiceSent]          = "Invoices",

        [EmailNotificationType.NewMessage]           = "Messages",
    };

    private readonly AppDbContext _context;
    private readonly IEmailService _email;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(AppDbContext context, IEmailService email, ILogger<NotificationService> logger)
    {
        _context = context;
        _email = email;
        _logger = logger;
    }

    public async Task NotifyAsync(
        int userId,
        EmailNotificationType type,
        string title,
        string content,
        int? advertId = null,
        int? paymentId = null,
        int? invoiceId = null)
    {
        try
        {
            var notification = new AppNotification
            {
                UserId = userId,
                Type = type,
                Title = title,
                Content = content,
                AdvertId = advertId,
                PaymentId = paymentId,
                InvoiceId = invoiceId,
                CreatedAt = DateTime.UtcNow
            };

            _context.AppNotifications.Add(notification);

            var category = Categories.GetValueOrDefault(type, "Other");
            var pref = await _context.UserNotificationSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == userId && s.Category == category);
            var emailEnabled = pref?.EmailEnabled ?? true;

            string? userEmail = null;
            if (emailEnabled)
            {
                userEmail = await _context.Users
                    .AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => u.Email)
                    .FirstOrDefaultAsync();
            }

            // Persist the in-app notification before attempting email so it is always saved
            // even if the SMTP send fails.
            await _context.SaveChangesAsync();

            if (emailEnabled && userEmail != null)
            {
                try
                {
                    var html = BuildEmailHtml(type, title, content, advertId, paymentId, invoiceId);
                    await _email.SendAsync(userEmail, title, html);
                    notification.EmailSent = true;
                    await _context.SaveChangesAsync();
                }
                catch (Exception emailEx)
                {
                    _logger.LogWarning(emailEx, "Email send failed userId={UserId} typ={Type}", userId, type);
                }
            }
        }
        catch (Exception ex)
        {
            // Exception type/message baked into the message TEXT itself, not just the
            // structured `ex` arg — Railway's log viewer only renders the template text and
            // splits multi-line stack traces into separate entries with no visible link back
            // to the exception that produced them (same issue fixed for GlobalExceptionHandler
            // earlier), making the actual failure impossible to find from the UI otherwise.
            var inner = ex.InnerException != null ? $" | inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}" : "";
            _logger.LogError(ex, "[NotifyAsync] Błąd powiadomienia userId={UserId} typ={Type} -- {ExType}: {ExMessage}{Inner}",
                userId, type, ex.GetType().Name, ex.Message, inner);
        }
    }

    private static string BuildEmailHtml(
        EmailNotificationType type,
        string title,
        string content,
        int? advertId,
        int? paymentId,
        int? invoiceId)
    {
        const string siteUrl = "https://carizo.eu";
        string? ctaUrl = null;
        string? ctaLabel = null;

        switch (type)
        {
            case EmailNotificationType.AccountCreated:
                ctaUrl = $"{siteUrl}/dashboard"; ctaLabel = "Przejdź do panelu"; break;

            case EmailNotificationType.PasswordChanged:
            case EmailNotificationType.PasswordReset:
                ctaUrl = $"{siteUrl}/login"; ctaLabel = "Zaloguj się"; break;

            case EmailNotificationType.AdvertAdded:
            case EmailNotificationType.AdvertPublished:
            case EmailNotificationType.AdvertRejected:
            case EmailNotificationType.AdvertMarkedSold:
            case EmailNotificationType.AdvertExpiring7Days:
            case EmailNotificationType.AdvertExpiring3Days:
            case EmailNotificationType.AdvertExpiring1Day:
            case EmailNotificationType.AdvertExpired:
                if (advertId.HasValue) { ctaUrl = $"{siteUrl}/advert/{advertId}"; ctaLabel = "Przejdź do ogłoszenia"; }
                break;

            case EmailNotificationType.AdvertDeleted:
                ctaUrl = $"{siteUrl}/my-adverts"; ctaLabel = "Moje ogłoszenia"; break;

            case EmailNotificationType.PromotionPurchased:
            case EmailNotificationType.PromotionActivated:
            case EmailNotificationType.TopStarted:
            case EmailNotificationType.PremiumStarted:
            case EmailNotificationType.FeaturedStarted:
            case EmailNotificationType.RefreshStarted:
            case EmailNotificationType.PromotionExpiring3Days:
            case EmailNotificationType.PromotionExpiring1Day:
            case EmailNotificationType.PromotionExpired:
                ctaUrl = $"{siteUrl}/my-adverts"; ctaLabel = "Moje ogłoszenia"; break;

            case EmailNotificationType.PaymentConfirmed:
            case EmailNotificationType.PaymentFailed:
            case EmailNotificationType.PaymentRefunded:
                ctaUrl = $"{siteUrl}/faktury"; ctaLabel = "Historia płatności"; break;

            case EmailNotificationType.InvoiceGenerated:
            case EmailNotificationType.InvoiceSent:
                ctaUrl = $"{siteUrl}/faktury"; ctaLabel = "Pobierz fakturę"; break;

            case EmailNotificationType.NewMessage:
                ctaUrl = $"{siteUrl}/messages"; ctaLabel = "Odczytaj wiadomość"; break;
        }

        return EmailService.BuildHtml(title, content, null, ctaUrl, ctaLabel);
    }
}
