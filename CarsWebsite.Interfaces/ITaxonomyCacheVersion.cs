namespace cars_website_api.CarsWebsite.Interfaces
{
    // Lets admin taxonomy edits invalidate every cached TaxonomyService result at once, since the
    // underlying cache has no prefix/wildcard eviction and there are too many independently-keyed
    // cached shapes (brands/models/generations/engines/trims/feature categories/etc) to track and
    // remove individually without missing one.
    //
    // Backed by IDistributedCache (CTO audit Etap 4) rather than an in-process int: with more than
    // one replica behind a load balancer, an admin edit landing on replica A must invalidate the
    // taxonomy cache on replica B too, which an in-process counter can never do.
    public interface ITaxonomyCacheVersion
    {
        Task<string> GetCurrentAsync(CancellationToken cancellationToken = default);
        Task BumpAsync(CancellationToken cancellationToken = default);
    }
}
