using System.Net;
using CarsWebsite;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.EntityFrameworkCore;

// Runs as a Hangfire recurring job (see Program.cs) - see BadgeExpiryJob for why the old
// AdvisoryLock/_lastRunDate dedup logic was removed (Hangfire's cron schedule already fires this
// once per day, and its MySQL-backed queue guarantees only one server picks it up).
public class ExpiryReminderJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExpiryReminderJob> _logger;

    public ExpiryReminderJob(IServiceScopeFactory scopeFactory, ILogger<ExpiryReminderJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var todayDt = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

            var expired = await context.Adverts
                .Where(a => a.IsActive && a.ExpiresAt.HasValue && a.ExpiresAt.Value < todayDt)
                .ToListAsync(ct);

            foreach (var a in expired)
            {
                a.IsActive = false;
                await notifications.NotifyAsync(a.UserId, EmailNotificationType.AdvertExpired,
                    "Ogłoszenie wygasło",
                    $"Twoje ogłoszenie \"{WebUtility.HtmlEncode(a.Title)}\" wygasło i zostało dezaktywowane. Możesz je odnowić lub dodać nowe.",
                    advertId: a.Id);
            }
            if (expired.Any()) await context.SaveChangesAsync(ct);

            await SendReminders(context, notifications, today, 1,
                EmailNotificationType.AdvertExpiring1Day, "Ogłoszenie wygasa jutro",
                t => $"Twoje ogłoszenie \"{t}\" wygaśnie jutro. Rozważ odnowienie.", ct);

            await SendReminders(context, notifications, today, 3,
                EmailNotificationType.AdvertExpiring3Days, "Ogłoszenie wygasa za 3 dni",
                t => $"Twoje ogłoszenie \"{t}\" wygaśnie za 3 dni.", ct);

            await SendReminders(context, notifications, today, 7,
                EmailNotificationType.AdvertExpiring7Days, "Ogłoszenie wygasa za 7 dni",
                t => $"Twoje ogłoszenie \"{t}\" wygaśnie za 7 dni.", ct);

            _logger.LogInformation("ExpiryReminderJob completed for {Date}", today);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExpiryReminderJob failed");
        }
    }

    private static async Task SendReminders(
        AppDbContext ctx,
        INotificationService notifications,
        DateOnly today,
        int daysAhead,
        EmailNotificationType type,
        string title,
        Func<string, string> contentFn,
        CancellationToken ct)
    {
        var min = today.AddDays(daysAhead).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var max = today.AddDays(daysAhead + 1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var adverts = await ctx.Adverts
            .Where(a => a.IsActive && a.ExpiresAt.HasValue && a.ExpiresAt >= min && a.ExpiresAt < max)
            .ToListAsync(ct);

        foreach (var a in adverts)
            await notifications.NotifyAsync(a.UserId, type, title, contentFn(WebUtility.HtmlEncode(a.Title)), advertId: a.Id);
    }
}
