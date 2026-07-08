using cars_website_api.CarsWebsite.Interfaces;

namespace cars_website_api.CarsWebsite.Services
{
    public class TaxonomyCacheVersion : ITaxonomyCacheVersion
    {
        private int _version;
        public int Current => _version;
        public void Bump() => Interlocked.Increment(ref _version);
    }
}
