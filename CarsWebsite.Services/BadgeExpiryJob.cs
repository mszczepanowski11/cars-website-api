using CarsWebsite;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.EntityFrameworkCore;

public class BadgeExpiryJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BadgeExpiryJob> _logger;

    public BadgeExpiryJob(IServiceScopeFactory scopeFactory, ILogger<BadgeExpiryJob> logger)
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
                    $"Wyróżnienie Twojego ogłoszenia \"{advert.Title}\" wygasło. Możesz przedłużyć promocję w każdej chwili.",
                    advertId: advert.Id);
            }

            if (expired.Count > 0)
            {
                await context.SaveChangesAsync(ct);
                _logger.LogInformation("BadgeExpiryJob: wygasło {Count} promocji", expired.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BadgeExpiryJob failed");
        }
    }
}
