using CarsWebsite;
using cars_website_api.CarsWebsite.Interfaces;

// Runs as a Hangfire recurring job (see Program.cs) - see BadgeExpiryJob for why the old
// AdvisoryLock wrapping was removed.
public class SubscriptionExpiryJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionExpiryJob> _logger;

    public SubscriptionExpiryJob(IServiceScopeFactory scopeFactory, ILogger<SubscriptionExpiryJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var subscriptionService = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();
            await subscriptionService.ResetExpiredSubscriptionsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SubscriptionExpiryJob] Błąd podczas resetowania wygasłych subskrypcji");
        }
    }
}
