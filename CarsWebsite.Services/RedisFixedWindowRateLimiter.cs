using System.Threading.RateLimiting;
using StackExchange.Redis;

namespace cars_website_api.CarsWebsite.Services;

// Distributed counterpart to ASP.NET Core's built-in in-process FixedWindowRateLimiter (CTO audit
// Etap 4 - "stan rate limitera" must be shared across replicas once there is more than one, or a
// client gets N x replica_count requests through instead of N, since each replica would otherwise
// enforce its own independent counter). One Redis key per partition; INCR+PEXPIRE happen atomically
// in a single Lua script so concurrent requests across every replica can never race past the limit.
// The window itself needs no timestamp bucketing: PEXPIRE on the first INCR naturally rolls the
// window forward per key, the same fixed-window-per-partition semantics as the in-process limiter.
public sealed class RedisFixedWindowRateLimiter : RateLimiter
{
    private static readonly LuaScript IncrementScript = LuaScript.Prepare(
        "local current = redis.call('INCR', @key) " +
        "if current == 1 then redis.call('PEXPIRE', @key, @windowMs) end " +
        "return current");

    private readonly IConnectionMultiplexer _redis;
    private readonly string _key;
    private readonly int _permitLimit;
    private readonly long _windowMs;

    public RedisFixedWindowRateLimiter(IConnectionMultiplexer redis, string key, int permitLimit, TimeSpan window)
    {
        _redis = redis;
        _key = key;
        _permitLimit = permitLimit;
        _windowMs = (long)window.TotalMilliseconds;
    }

    public override TimeSpan? IdleDuration => null;

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        var db = _redis.GetDatabase();
        var result = (long)db.ScriptEvaluate(IncrementScript, new { key = (RedisKey)_key, windowMs = _windowMs });
        return new BooleanLease(result <= _permitLimit);
    }

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var result = (long)await db.ScriptEvaluateAsync(IncrementScript, new { key = (RedisKey)_key, windowMs = _windowMs });
        return new BooleanLease(result <= _permitLimit);
    }

    // No queueing - every rejected attempt fails immediately, matching how this project's "auth"/
    // "strict"/"ai" policies already run (QueueLimit: 0); only "global" queues in-process today,
    // a minor behavioral difference accepted here in exchange for correctness across replicas.
    public override RateLimiterStatistics? GetStatistics() => null;

    private sealed class BooleanLease : RateLimitLease
    {
        public BooleanLease(bool isAcquired) => IsAcquired = isAcquired;
        public override bool IsAcquired { get; }
        public override IEnumerable<string> MetadataNames => [];
        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
    }
}
