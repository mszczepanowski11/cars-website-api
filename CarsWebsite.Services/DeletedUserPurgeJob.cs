using CarsWebsite;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Hard-deletes anonymized user accounts that have been soft-deleted for more than 5 years,
/// fulfilling GDPR retention limits.
/// </summary>
public class DeletedUserPurgeJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeletedUserPurgeJob> _logger;
    private DateOnly _lastRunDate = DateOnly.MinValue;

    public DeletedUserPurgeJob(IServiceScopeFactory scopeFactory, ILogger<DeletedUserPurgeJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(now);
            if (now.Hour == 3 && _lastRunDate != today)
            {
                await RunAsync(stoppingToken);
                _lastRunDate = today;
            }
            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await AdvisoryLock.TryRunExclusiveAsync(context, "carizo:deleted_user_purge_job", async () =>
            {
                var cutoff = DateTime.UtcNow.AddYears(-5);
                var toDelete = await context.Users
                    .Where(u => u.Email.EndsWith("@carizo.deleted") && u.BlockedAt != null && u.BlockedAt < cutoff)
                    .ToListAsync(ct);

                if (toDelete.Count == 0) return;

                context.Users.RemoveRange(toDelete);
                await context.SaveChangesAsync(ct);

                _logger.LogInformation("[DeletedUserPurgeJob] Hard-deleted {Count} anonymized accounts older than 5 years.", toDelete.Count);
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DeletedUserPurgeJob] Error during purge run.");
        }
    }
}
