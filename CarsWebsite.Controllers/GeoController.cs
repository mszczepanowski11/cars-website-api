using CarsWebsite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Controllers;

// Public geo cascade (Faza 0 of the global rearchitecture): continent -> country -> region -> city,
// plus currency/language reference lists. All reads are anonymous - the add-advert form, register
// and company profile all need this without a login. This is what replaces the 16 hardcoded Polish
// voivodeships and the free-text city field: cities are looked up here, never typed by hand.
[ApiController]
[Route("api/geo")]
[AllowAnonymous]
public class GeoController : ControllerBase
{
    private readonly AppDbContext _db;
    public GeoController(AppDbContext db) => _db = db;

    // Resolve a country by ISO2 ("PL") or numeric id ("178"), so callers can pass whichever they have.
    private async Task<int?> ResolveCountryIdAsync(string? country)
    {
        if (string.IsNullOrWhiteSpace(country)) return null;
        if (int.TryParse(country, out var id)) return id;
        var iso = country.Trim().ToUpperInvariant();
        return await _db.Countries.Where(c => c.Iso2 == iso).Select(c => (int?)c.Id).FirstOrDefaultAsync();
    }

    [HttpGet("continents")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetContinents()
        => Ok(await _db.Continents.AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Code, c.Name }).ToListAsync());

    [HttpGet("currencies")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetCurrencies()
        => Ok(await _db.Currencies.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Iso)
            .Select(c => new { c.Id, c.Iso, c.Symbol, c.Name, c.Decimals, c.SymbolPosition }).ToListAsync());

    [HttpGet("languages")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetLanguages()
        => Ok(await _db.Languages.AsNoTracking().Where(l => l.IsActive).OrderBy(l => l.EnglishName)
            .Select(l => new { l.Id, l.Iso1, l.Endonym, l.EnglishName, l.IsRtl }).ToListAsync());

    [HttpGet("countries")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetCountries([FromQuery] string? continent)
    {
        var q = _db.Countries.AsNoTracking().Where(c => c.IsActive);
        if (!string.IsNullOrWhiteSpace(continent))
        {
            var code = continent.Trim().ToUpperInvariant();
            q = q.Where(c => c.Continent != null && c.Continent.Code == code);
        }
        var items = await q.OrderBy(c => c.Name).Select(c => new {
            c.Id, c.Iso2, c.Iso3, c.Name, c.NativeName, c.PhonePrefix, c.MeasurementSystem, c.DrivingSide,
            DefaultCurrency = c.DefaultCurrency != null ? c.DefaultCurrency.Iso : null,
            DefaultLanguage = c.DefaultLanguage != null ? c.DefaultLanguage.Iso1 : null,
            HasRegions = c.Regions.Any(),
        }).ToListAsync();
        return Ok(items);
    }

    [HttpGet("regions")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetRegions([FromQuery] string country)
    {
        var countryId = await ResolveCountryIdAsync(country);
        if (countryId == null) return Ok(Array.Empty<object>());
        var items = await _db.Regions.AsNoTracking().Where(r => r.CountryId == countryId)
            .OrderBy(r => r.Name).Select(r => new { r.Id, r.Code, r.Name, r.Type }).ToListAsync();
        return Ok(items);
    }

    // Typeahead city lookup - min 1 char if a region is fixed, else 2. Sorted by population so the
    // biggest cities surface first. Hard cap keeps the payload small even for large countries.
    [HttpGet("cities")]
    public async Task<IActionResult> GetCities(
        [FromQuery] string country, [FromQuery] int? region, [FromQuery] string? q, [FromQuery] int limit = 20)
    {
        var countryId = await ResolveCountryIdAsync(country);
        if (countryId == null) return Ok(Array.Empty<object>());
        limit = Math.Clamp(limit, 1, 50);

        var query = _db.Cities.AsNoTracking().Where(c => c.CountryId == countryId);
        if (region.HasValue) query = query.Where(c => c.RegionId == region);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(c => c.Name.StartsWith(term) || c.AsciiName.StartsWith(term));
        }
        var items = await query.OrderByDescending(c => c.Population).ThenBy(c => c.Name).Take(limit)
            .Select(c => new { c.Id, c.Name, c.RegionId, c.Latitude, c.Longitude }).ToListAsync();
        return Ok(items);
    }
}
