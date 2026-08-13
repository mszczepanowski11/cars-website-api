using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.Extensions.Caching.Distributed;

namespace cars_website_api.CarsWebsite.Services
{
    public class TaxonomyCacheVersion : ITaxonomyCacheVersion
    {
        private const string VersionKey = "taxonomy:cache-version";

        // Long TTL, not "forever": a version token surviving a Redis eviction/restart just means
        // the next GetCurrentAsync mints a fresh one (see below) - a harmless one-time cache miss,
        // not a correctness problem, so there is no need to pin this key in memory indefinitely.
        private static readonly DistributedCacheEntryOptions VersionEntryOptions = new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30),
        };

        private readonly IDistributedCache _cache;

        public TaxonomyCacheVersion(IDistributedCache cache)
        {
            _cache = cache;
        }

        public async Task<string> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            var current = await _cache.GetStringAsync(VersionKey, cancellationToken);
            if (current != null) return current;

            // Cold cache (first boot, or the key aged out): mint a token so every replica
            // converges on the same version. Two replicas racing here both write a token and
            // both are valid - worst case is one extra cache miss right after, not a correctness
            // issue, so no need for a compare-and-swap here.
            var minted = Guid.NewGuid().ToString("N");
            await _cache.SetStringAsync(VersionKey, minted, VersionEntryOptions, cancellationToken);
            return minted;
        }

        public Task BumpAsync(CancellationToken cancellationToken = default) =>
            _cache.SetStringAsync(VersionKey, Guid.NewGuid().ToString("N"), VersionEntryOptions, cancellationToken);
    }
}
