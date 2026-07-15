using System.Net;
using CarsWebsite;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.EntityFrameworkCore;

public class EventFeaturedExpiryJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventFeaturedExpiryJob> _logger;

    public EventFeaturedExpiryJob(IServiceScopeFactory scopeFactory, ILogger<EventFeaturedExpiryJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ExpireAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task ExpireAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

            await AdvisoryLock.TryRunExclusiveAsync(context, "carizo:event_featured_expiry_job", async () =>
            {
                await RunAsync(context, notifications, ct);
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EventFeaturedExpiryJob failed");
        }
    }

    private async Task RunAsync(AppDbContext context, INotificationService notifications, CancellationToken ct)
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
