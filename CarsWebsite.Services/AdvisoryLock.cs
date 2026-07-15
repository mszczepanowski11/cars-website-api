using System.Data;
using Microsoft.EntityFrameworkCore;

// Wraps a job/startup body in a MySQL named lock (GET_LOCK/RELEASE_LOCK) so that when the API
// runs as more than one instance (required for horizontal scaling), only one instance actually
// executes it at a time - without this, every IHostedService background job
// (SubscriptionExpiryJob, MonthlyInvoiceJob, ExpiryReminderJob, BadgeExpiryJob,
// EventFeaturedExpiryJob, DeletedUserPurgeJob) independently decides "nobody has run this yet" on
// every replica, and every replica's startup migration/schema-guard block races the others'
// concurrent DDL against the same tables.
public static class AdvisoryLock
{
    // Background jobs: non-blocking (0s wait) by default - if another instance already holds the
    // lock, this instance skips the run entirely rather than queueing up behind it, since the job
    // runs again on its own next tick anyway.
    public static async Task<bool> TryRunExclusiveAsync(
        DbContext context, string lockName, Func<Task> action, CancellationToken ct = default, int waitSeconds = 0)
    {
        var conn = context.Database.GetDbConnection();
        var openedHere = conn.State != ConnectionState.Open;
        if (openedHere) await conn.OpenAsync(ct);
        try
        {
            if (!await AcquireAsync(conn, lockName, waitSeconds, ct)) return false;
            try { await action(); return true; }
            finally { await ReleaseAsync(conn, lockName, ct); }
        }
        finally
        {
            if (openedHere) await conn.CloseAsync();
        }
    }

    // Startup migrations (Program.cs's synchronous Main, before the async host is running):
    // waits up to `waitSeconds` for another instance's in-flight migration to finish before giving
    // up and proceeding without running DDL itself - unlike the background-job case, a fresh
    // instance must not start serving traffic while assuming a schema change that's still
    // mid-flight on a sibling instance.
    public static bool TryRunExclusive(DbContext context, string lockName, Action action, int waitSeconds = 30)
    {
        var conn = context.Database.GetDbConnection();
        var openedHere = conn.State != ConnectionState.Open;
        if (openedHere) conn.Open();
        try
        {
            using (var acquireCmd = conn.CreateCommand())
            {
                acquireCmd.CommandText = "SELECT GET_LOCK(@name, @wait)";
                AddParam(acquireCmd, "@name", lockName);
                AddParam(acquireCmd, "@wait", waitSeconds);
                var result = acquireCmd.ExecuteScalar();
                if (result == null || result == DBNull.Value || Convert.ToInt64(result) != 1)
                    return false;
            }

            try { action(); return true; }
            finally
            {
                using var releaseCmd = conn.CreateCommand();
                releaseCmd.CommandText = "SELECT RELEASE_LOCK(@name)";
                AddParam(releaseCmd, "@name", lockName);
                releaseCmd.ExecuteScalar();
            }
        }
        finally
        {
            if (openedHere) conn.Close();
        }
    }

    private static async Task<bool> AcquireAsync(System.Data.Common.DbConnection conn, string lockName, int waitSeconds, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT GET_LOCK(@name, @wait)";
        AddParam(cmd, "@name", lockName);
        AddParam(cmd, "@wait", waitSeconds);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null && result != DBNull.Value && Convert.ToInt64(result) == 1;
    }

    private static async Task ReleaseAsync(System.Data.Common.DbConnection conn, string lockName, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT RELEASE_LOCK(@name)";
        AddParam(cmd, "@name", lockName);
        await cmd.ExecuteScalarAsync(ct);
    }

    private static void AddParam(IDbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
