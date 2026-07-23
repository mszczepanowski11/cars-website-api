using cars_website_api.CarsWebsite.Domain.Entities;

// Fail-open by design: Meilisearch here is an accelerator for free-text search relevance/typo
// tolerance, never a hard dependency. Every implementation method must swallow its own
// connectivity/API errors (log and return a value that tells the caller to fall back), never throw
// out to AdvertService - a Meilisearch outage must not take advert search down with it.
public interface IAdvertSearchIndexService
{
    // False when no Host is configured (appsettings/env) - callers skip straight to the MySQL
    // FULLTEXT fallback without attempting a network call at all.
    bool IsEnabled { get; }

    Task IndexAsync(CarAdvert advert, CancellationToken cancellationToken = default);

    Task DeleteAsync(int advertId, CancellationToken cancellationToken = default);

    // Returns null (not an empty list) when Meilisearch is disabled/unreachable/erroring - the null
    // is the fall-back-to-MySQL signal; an empty list is a genuine "no matches" result.
    Task<List<int>?> SearchIdsAsync(string text, int limit, CancellationToken cancellationToken = default);

    // Full reindex of every currently-searchable advert (IsActive && !IsHidden && not expired).
    // Admin-triggered on demand (initial population / recovery after an outage), not run
    // automatically on startup - mirrors how this codebase avoids adding more "every startup" guards.
    Task<int> ReindexAllAsync(CancellationToken cancellationToken = default);
}
