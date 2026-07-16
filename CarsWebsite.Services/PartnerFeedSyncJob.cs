using CarsWebsite;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.EntityFrameworkCore;

// Runs as a Hangfire recurring job (see Program.cs). Pull-side counterpart to the push
// (X-Api-Key) import endpoint: for every active Partner with a FeedUrl and AutoSyncEnabled,
// fetches the URL fresh and imports it, exactly like a manual push would, so a partner who only
// ever gave us a URL (via the "Dla firm" signup) still gets kept in sync automatically.
public class PartnerFeedSyncJob
{
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
            var feedFetch = scope.ServiceProvider.GetRequiredService<IPartnerFeedFetchService>();
            var partnerImport = scope.ServiceProvider.GetRequiredService<IPartnerImportService>();

            var partners = await context.Partners
                .Where(p => p.IsActive && p.AutoSyncEnabled && p.FeedUrl != null)
                .ToListAsync(ct);

            foreach (var partner in partners)
            {
                try
                {
                    var fetch = await feedFetch.FetchAsync(partner.FeedUrl!);
                    if (!fetch.Success)
                    {
                        _logger.LogWarning("[PartnerFeedSyncJob] Fetch failed for partner #{Id} ({Company}): {Error}",
                            partner.Id, partner.CompanyName, fetch.Error);
                        continue;
                    }

                    var log = await partnerImport.ImportAsync(partner, fetch.Content!, fetch.Format);
                    _logger.LogInformation("[PartnerFeedSyncJob] Synced partner #{Id} ({Company}): {Created} created, {Updated} updated, {Failed} failed",
                        partner.Id, partner.CompanyName, log.ItemsCreated, log.ItemsUpdated, log.ItemsFailed);
                }
                catch (Exception exInner)
                {
                    _logger.LogWarning(exInner, "[PartnerFeedSyncJob] Failed to sync partner #{Id}", partner.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PartnerFeedSyncJob] failed");
        }
    }
}
