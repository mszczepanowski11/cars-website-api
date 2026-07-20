using CarsWebsite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace cars_website_api.CarsWebsite.Services;

// Geocoding connector for the directory map (blueprint: geo layer). Hangfire recurring job that
// fills Latitude/Longitude for active companies that have an address but no coordinates yet, using
// OpenStreetMap Nominatim (same service the client-side EventMap uses).
//
// Nominatim's usage policy allows at most ~1 request/second and requires a real User-Agent, so the
// job is deliberately slow and small-batch: it does a handful per run and leans on the recurring
// schedule to work through the backlog. Disabled by default; enable with DIRECTORY_GEOCODE=1.
public class DirectoryGeocodeJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<DirectoryGeocodeJob> _logger;

    public DirectoryGeocodeJob(IServiceScopeFactory scopeFactory, IHttpClientFactory httpFactory, ILogger<DirectoryGeocodeJob> logger)
    {
        _scopeFactory = scopeFactory;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("DIRECTORY_GEOCODE") != "1")
        {
            _logger.LogInformation("[DirectoryGeocodeJob] skipped - disabled (set DIRECTORY_GEOCODE=1 to enable).");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Only rows we can actually geocode: some address signal, no coordinates yet.
        var batch = await db.DirectoryCompanies
            .Where(d => d.Status != "closed" && d.Latitude == null
                        && (d.City != null || d.Address != null))
            .OrderBy(d => d.Id)
            .Take(20)
            .ToListAsync(ct);

        if (batch.Count == 0) { _logger.LogInformation("[DirectoryGeocodeJob] nothing to geocode."); return; }

        var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Carizo/1.0 (+https://carizo.eu)");

        int filled = 0;
        foreach (var company in batch)
        {
            if (ct.IsCancellationRequested) break;
            var query = string.Join(", ", new[] { company.Address, company.City, company.CountryCode }.Where(s => !string.IsNullOrWhiteSpace(s)));
            var coords = await GeocodeAsync(client, query, ct)
                         ?? (company.City != null ? await GeocodeAsync(client, $"{company.City}, {company.CountryCode}", ct) : null);
            if (coords != null)
            {
                company.Latitude = coords.Value.Lat;
                company.Longitude = coords.Value.Lon;
                company.UpdatedAt = DateTime.UtcNow;
                filled++;
            }
            // Nominatim: keep well under 1 req/s.
            await Task.Delay(1200, ct);
        }

        if (filled > 0) await db.SaveChangesAsync(ct);
        _logger.LogInformation("[DirectoryGeocodeJob] done: {Filled}/{Total} companies geocoded.", filled, batch.Count);
    }

    private async Task<(double Lat, double Lon)?> GeocodeAsync(HttpClient client, string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        try
        {
            var url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(query)}&format=json&limit=1";
            var body = await client.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                var first = doc.RootElement[0];
                if (double.TryParse(first.GetProperty("lat").GetString(), System.Globalization.CultureInfo.InvariantCulture, out var lat)
                    && double.TryParse(first.GetProperty("lon").GetString(), System.Globalization.CultureInfo.InvariantCulture, out var lon))
                    return (lat, lon);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[DirectoryGeocodeJob] geocode failed for '{Query}': {Msg}", query, ex.Message);
        }
        return null;
    }
}
