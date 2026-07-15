using CarsWebsite;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Hangfire recurring job (see Program.cs) that hard-deletes anonymized user accounts that have
/// been soft-deleted for more than 5 years, fulfilling GDPR retention limits.
/// </summary>
public class DeletedUserPurgeJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeletedUserPurgeJob> _logger;

    public DeletedUserPurgeJob(IServiceScopeFactory scopeFactory, ILogger<DeletedUserPurgeJob> logger)
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

            var cutoff = DateTime.UtcNow.AddYears(-5);
            var toDelete = await context.Users
                .Where(u => u.Email.EndsWith("@carizo.deleted") && u.BlockedAt != null && u.BlockedAt < cutoff)
                .ToListAsync(ct);

            if (toDelete.Count == 0) return;

            var userIds = toDelete.Select(u => u.Id).ToList();
            // Adverts.UserId is now Restrict (not Cascade), so their adverts must be removed
            // explicitly before the users themselves - the FK would otherwise reject the delete.
            var adverts = await context.CarAdverts.Where(a => userIds.Contains(a.UserId)).ToListAsync(ct);
            context.CarAdverts.RemoveRange(adverts);
            context.Users.RemoveRange(toDelete);
            await context.SaveChangesAsync(ct);

            _logger.LogInformation("[DeletedUserPurgeJob] Hard-deleted {Count} anonymized accounts older than 5 years.", toDelete.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DeletedUserPurgeJob] Error during purge run.");
        }
    }
}
