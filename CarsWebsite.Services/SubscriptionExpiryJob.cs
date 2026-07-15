using CarsWebsite;
using cars_website_api.CarsWebsite.Interfaces;

public class SubscriptionExpiryJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionExpiryJob> _logger;

    public SubscriptionExpiryJob(IServiceScopeFactory scopeFactory, ILogger<SubscriptionExpiryJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var subscriptionService = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();
            await AdvisoryLock.TryRunExclusiveAsync(context, "carizo:subscription_expiry_job",
                () => subscriptionService.ResetExpiredSubscriptionsAsync(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SubscriptionExpiryJob] Błąd podczas resetowania wygasłych subskrypcji");
        }
    }
}
