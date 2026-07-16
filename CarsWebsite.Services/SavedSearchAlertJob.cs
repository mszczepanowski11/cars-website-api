using System.Net;
using System.Text.Json;
using CarsWebsite;
using cars_website_api.CarsWebsite.DTOs.Advert;
using Microsoft.EntityFrameworkCore;

// Runs as a Hangfire recurring job (see Program.cs). Re-runs each SavedSearch's stored criteria
// through the normal advert search and notifies the owner about any advert created since the
// search was last checked - LastCheckedAt is the watermark that keeps the same adverts from
// being counted across multiple runs.
public class SavedSearchAlertJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SavedSearchAlertJob> _logger;

    public SavedSearchAlertJob(IServiceScopeFactory scopeFactory, ILogger<SavedSearchAlertJob> logger)
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
            var advertService = scope.ServiceProvider.GetRequiredService<IAdvertService>();
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var searches = await context.SavedSearches.Where(s => s.NotifyOnNew).ToListAsync(ct);

            foreach (var search in searches)
            {
                try
                {
                    var criteria = JsonSerializer.Deserialize<SearchCarAdvertDto>(search.CriteriaJson) ?? new SearchCarAdvertDto();
                    criteria.Page = 1;
                    criteria.PageSize = 50;

                    var results = await advertService.SearchCarAdvertsAsync(criteria);
                    var newCount = results.Items.Count(a => a.CreatedAt > search.LastCheckedAt);

                    if (newCount > 0)
                    {
                        search.NewResultsCount += newCount;
                        _ = notifications.NotifyAsync(search.UserId, EmailNotificationType.PromotionActivated,
                            "Nowe ogłoszenia dla zapisanego wyszukiwania",
                            $"Znaleziono {newCount} nowych ogłoszeń pasujących do wyszukiwania \"{WebUtility.HtmlEncode(search.Name)}\".");
                    }

                    search.LastCheckedAt = DateTime.UtcNow;
                }
                catch (Exception exInner)
                {
                    _logger.LogWarning(exInner, "[SavedSearchAlertJob] Failed to check saved search #{Id}", search.Id);
                }
            }

            await context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SavedSearchAlertJob] failed");
        }
    }
}
