using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;

namespace cars_website_api.CarsWebsite.Services;

// GetOrCreateAsync equivalent to IMemoryCache's, for IDistributedCache - which only stores
// strings/bytes and has no such helper built in on .NET 8 (it was added upstream only in .NET 9).
// Mirrors IMemoryCache's "entry.AbsoluteExpirationRelativeToNow = ..." shape via
// DistributedCacheEntry so callers migrating from IMemoryCache keep an (almost) identical lambda
// body.
public static class DistributedCacheExtensions
{
    // ReferenceHandler.IgnoreCycles: mirrors Program.cs's MVC JSON options exactly, needed because
    // cached values here are often EF navigation graphs (e.g. Brand -> Models -> Generations, each
    // with a back-reference to its parent) that would otherwise throw on circular references.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
    };

    public static async Task<T?> GetOrCreateAsync<T>(
        this IDistributedCache cache,
        string key,
        Func<DistributedCacheEntry, Task<T>> factory,
        CancellationToken cancellationToken = default)
    {
        var cached = await cache.GetStringAsync(key, cancellationToken);
        if (cached != null)
        {
            return JsonSerializer.Deserialize<T>(cached, JsonOptions);
        }

        var entry = new DistributedCacheEntry();
        var value = await factory(entry);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = entry.AbsoluteExpirationRelativeToNow,
        };
        await cache.SetStringAsync(key, JsonSerializer.Serialize(value, JsonOptions), options, cancellationToken);
        return value;
    }
}

public class DistributedCacheEntry
{
    public TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }
}
