using System.Net;
using CarsWebsite;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.EntityFrameworkCore;

// Runs as a Hangfire recurring job (see Program.cs) instead of polling on its own timer - Hangfire's
// MySQL-backed queue already guarantees only one server picks up a given scheduled occurrence, which
// is what the old AdvisoryLock wrapping here existed to do by hand.
public class BadgeExpiryJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BadgeExpiryJob> _logger;

    public BadgeExpiryJob(IServiceScopeFactory scopeFactory, ILogger<BadgeExpiryJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
            await ExpireAsync(context, notifications, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BadgeExpiryJob failed");
        }
    }

    private async Task ExpireAsync(AppDbContext context, INotificationService notifications, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var expired = await context.CarAdverts
            .Where(a => a.Badge != null && a.BadgeExpiresAt.HasValue && a.BadgeExpiresAt.Value < now)
            .ToListAsync(ct);

        foreach (var advert in expired)
        {
            advert.Badge = null;
            advert.BadgeExpiresAt = null;
            _ = notifications.NotifyAsync(advert.UserId, EmailNotificationType.PromotionExpired,
                "Promocja wygasła",
                $"Wyróżnienie Twojego ogłoszenia \"{WebUtility.HtmlEncode(advert.Title)}\" wygasło. Możesz przedłużyć promocję w każdej chwili.",
                advertId: advert.Id);
        }

        if (expired.Count > 0)
        {
            await context.SaveChangesAsync(ct);
            _logger.LogInformation("BadgeExpiryJob: wygasło {Count} promocji", expired.Count);
        }
    }
}
