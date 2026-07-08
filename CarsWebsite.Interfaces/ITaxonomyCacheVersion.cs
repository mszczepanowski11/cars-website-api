namespace cars_website_api.CarsWebsite.Interfaces
{
    // Lets admin taxonomy edits invalidate every cached TaxonomyService result at once, since
    // IMemoryCache has no prefix/wildcard eviction and there are too many independently-keyed
    // cached shapes (brands/models/generations/engines/trims/feature categories/etc) to track
    // and remove individually without missing one.
    public interface ITaxonomyCacheVersion
    {
        int Current { get; }
        void Bump();
    }
}
