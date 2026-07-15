using System.Net;
using CarsWebsite;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.EntityFrameworkCore;

// Runs as a Hangfire recurring job (see Program.cs) - see BadgeExpiryJob for why the old
// AdvisoryLock wrapping was removed.
public class EventFeaturedExpiryJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventFeaturedExpiryJob> _logger;

    public EventFeaturedExpiryJob(IServiceScopeFactory scopeFactory, ILogger<EventFeaturedExpiryJob> logger)
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
            _logger.LogError(ex, "EventFeaturedExpiryJob failed");
        }
    }

    private async Task ExpireAsync(AppDbContext context, INotificationService notifications, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var expired = await context.Events
            .Where(e => e.IsFeatured && e.FeaturedUntil.HasValue && e.FeaturedUntil.Value < now)
            .ToListAsync(ct);

        foreach (var ev in expired)
        {
            ev.IsFeatured = false;
            ev.FeaturedUntil = null;
            _ = notifications.NotifyAsync(ev.CreatedByUserId, EmailNotificationType.PromotionExpired,
                "Wyróżnienie wydarzenia wygasło",
                $"Wyróżnienie Twojego wydarzenia \"{WebUtility.HtmlEncode(ev.Name)}\" wygasło. Możesz przedłużyć promocję w każdej chwili.");
        }

        if (expired.Count > 0)
        {
            await context.SaveChangesAsync(ct);
            _logger.LogInformation("EventFeaturedExpiryJob: wygasło {Count} wyróżnień wydarzeń", expired.Count);
        }
    }
}
