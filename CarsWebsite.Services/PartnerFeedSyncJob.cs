using CarsWebsite;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.EntityFrameworkCore;

// Runs as a Hangfire recurring job (see Program.cs). Pull-side counterpart to the push
// (X-Api-Key) import endpoint: for every active Partner with a FeedUrl and AutoSyncEnabled,
// fetches the URL fresh and imports it, exactly like a manual push would, so a partner who only
// ever gave us a URL (via the "Dla firm" signup) still gets kept in sync automatically.
//
// Shares IPartnerService.SyncNowAsync with the admin "sync now" button (AdminPartnerController)
// instead of duplicating the fetch+import logic - one code path, two triggers (cron vs. manual).
public class PartnerFeedSyncJob
{
    // Alert admins once a partner's UNATTENDED sync has failed this many scheduled runs in a row
    // (2 runs * the 6h cron interval = ~12h broken before anyone is told) - one bad run is often
    // just a partner's server having a bad moment, not worth paging anyone over.
    private const int AlertThreshold = 2;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PartnerFeedSyncJob> _logger;

    public PartnerFeedSyncJob(IServiceScopeFactory scopeFactory, ILogger<PartnerFeedSyncJob> logger)
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
            var partnerService = scope.ServiceProvider.GetRequiredService<IPartnerService>();
            var email = scope.ServiceProvider.GetRequiredService<IEmailService>();

            var partners = await context.Partners
                .Where(p => p.IsActive && p.AutoSyncEnabled && p.FeedUrl != null)
                .ToListAsync(ct);

            foreach (var partner in partners)
            {
                partner.LastSyncAttemptAt = DateTime.UtcNow;
                try
                {
                    var log = await partnerService.SyncNowAsync(partner.Id);
                    _logger.LogInformation("[PartnerFeedSyncJob] Synced partner #{Id} ({Company}): {Created} created, {Updated} updated, {Failed} failed",
                        partner.Id, partner.CompanyName, log.ItemsCreated, log.ItemsUpdated, log.ItemsFailed);
                    partner.ConsecutiveSyncFailures = 0;
                    partner.LastSyncError = null;
                }
                catch (Exception exInner)
                {
                    _logger.LogWarning(exInner, "[PartnerFeedSyncJob] Failed to sync partner #{Id} ({Company})", partner.Id, partner.CompanyName);
                    partner.ConsecutiveSyncFailures++;
                    partner.LastSyncError = exInner.Message.Length > 1000 ? exInner.Message[..1000] : exInner.Message;

                    if (partner.ConsecutiveSyncFailures >= AlertThreshold)
                        await AlertAdminsAsync(context, email, partner);
                }
                await context.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PartnerFeedSyncJob] failed");
        }
    }

    private async Task AlertAdminsAsync(AppDbContext context, IEmailService email, Partner partner)
    {
        var adminEmails = await context.Users.Where(u => u.IsAdmin).Select(u => u.Email).ToListAsync();
        var subject = $"[CARIZO] Synchronizacja partnera \"{partner.CompanyName}\" nie działa od {partner.ConsecutiveSyncFailures} prób";
        var html = $"""
            <p>Automatyczna synchronizacja feedu partnera <strong>{System.Net.WebUtility.HtmlEncode(partner.CompanyName)}</strong>
            (ID {partner.Id}) zawiodła {partner.ConsecutiveSyncFailures} razy z rzędu (co ~6h).</p>
            <p><strong>Ostatni błąd:</strong> {System.Net.WebUtility.HtmlEncode(partner.LastSyncError)}</p>
            <p>Sprawdź panel Partnerzy API w adminie i użyj "Synchronizuj teraz" po naprawie feedu partnera.</p>
            """;
        foreach (var adminEmail in adminEmails)
            await email.SendAsync(adminEmail, subject, html);
    }
}
