using cars_website_api.CarsWebsite.DTOs.Directory;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Controllers;

// Business Directory (blueprint section 17). Reads are public - the /firmy pages and, later,
// Data API consumers need them without a login. Writes are admin-only.
[ApiController]
[Route("api/directory")]
public class DirectoryController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITranslationProvider _translator;

    public DirectoryController(AppDbContext db, ITranslationProvider translator)
    {
        _db = db;
        _translator = translator;
    }

    // GET /api/directory?q=&category=&country=&city=&page=1&pageSize=24
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> List(
        [FromQuery] string? q, [FromQuery] string? category, [FromQuery] string? country,
        [FromQuery] string? city, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 24)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 60);

        var query = _db.DirectoryCompanies.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            // Explicit status filter (admin review, e.g. ?status=unverified or ?status=closed).
            query = query.Where(d => d.Status == status);
        else
            // Default public view: everything that isn't closed. Seeded 'unverified' rows are still
            // shown - the directory is useful before every entry is hand-confirmed - flagged in UI.
            query = query.Where(d => d.Status != "closed");

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(d => d.Category == category);
        if (!string.IsNullOrWhiteSpace(country))
            query = query.Where(d => d.CountryCode == country);
        if (!string.IsNullOrWhiteSpace(city))
        {
            var c = city.Trim();
            query = query.Where(d => d.City == c);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var norm = CarizoId.Normalize(q);
            // Prefix + contains on the normalized name - index-friendly for the common prefix case.
            query = query.Where(d => d.NameNormalized.Contains(norm));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(d => d.Status == "active")
            .ThenBy(d => d.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(d => new DirectoryCompanyListDto
            {
                PublicId = d.PublicId, Slug = d.Slug, Name = d.Name, Category = d.Category,
                CountryCode = d.CountryCode, City = d.City, Website = d.Website, Status = d.Status,
            })
            .ToListAsync();

        return Ok(new DirectoryListResultDto { Items = items, Total = total, Page = page, PageSize = pageSize });
    }

    // GET /api/directory/facets - counts per category, for the filter sidebar.
    [HttpGet("facets")]
    [AllowAnonymous]
    public async Task<IActionResult> Facets([FromQuery] string? country)
    {
        var query = _db.DirectoryCompanies.AsNoTracking().Where(d => d.Status != "closed");
        if (!string.IsNullOrWhiteSpace(country))
            query = query.Where(d => d.CountryCode == country);

        var categories = await query
            .GroupBy(d => d.Category)
            .Select(g => new DirectoryFacetDto { Value = g.Key, Count = g.Count() })
            .OrderByDescending(f => f.Count).ToListAsync();

        var countries = await _db.DirectoryCompanies.AsNoTracking().Where(d => d.Status != "closed" && d.CountryCode != null)
            .GroupBy(d => d.CountryCode!)
            .Select(g => new DirectoryFacetDto { Value = g.Key, Count = g.Count() })
            .OrderByDescending(f => f.Count).ToListAsync();

        return Ok(new { categories, countries });
    }

    // GET /api/directory/{slug}?lang=de
    // If a translation for `lang` exists in the I18n JSON, name/description are returned in that
    // language (falling back to the base value per field). This is the read side of the i18n model.
    [HttpGet("{slug}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetBySlug(string slug, [FromQuery] string? lang)
    {
        var d = await _db.DirectoryCompanies.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug && x.Status != "closed");
        if (d == null) return NotFound();

        var (name, description) = Localize(d, lang);
        return Ok(new DirectoryCompanyDetailDto
        {
            PublicId = d.PublicId, Slug = d.Slug, Name = name, Category = d.Category,
            CountryCode = d.CountryCode, City = d.City, Address = d.Address, PostalCode = d.PostalCode,
            Phone = d.Phone, Email = d.Email, Website = d.Website, ProfileUrl = d.ProfileUrl,
            Language = d.Language, Description = description, Latitude = d.Latitude, Longitude = d.Longitude,
            Linked = d.PartnerId != null, AvailableLanguages = AvailableLanguages(d),
            Status = d.Status, CreatedAt = d.CreatedAt, UpdatedAt = d.UpdatedAt,
        });
    }

    // Base language + every language present in the I18n JSON (deduped, base first).
    private static List<string> AvailableLanguages(DirectoryCompany d)
    {
        var langs = new List<string>();
        var baseLang = string.IsNullOrWhiteSpace(d.Language) ? "pl" : d.Language.ToLowerInvariant();
        langs.Add(baseLang);
        if (!string.IsNullOrWhiteSpace(d.I18n))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(d.I18n);
                foreach (var prop in doc.RootElement.EnumerateObject())
                    if (!langs.Contains(prop.Name.ToLowerInvariant())) langs.Add(prop.Name.ToLowerInvariant());
            }
            catch { /* malformed -> just the base language */ }
        }
        return langs;
    }

    // Returns (name, description) in `lang` if a translation exists, else the base values.
    private static (string Name, string? Description) Localize(DirectoryCompany d, string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang) || string.IsNullOrWhiteSpace(d.I18n)
            || lang.Equals(d.Language, StringComparison.OrdinalIgnoreCase))
            return (d.Name, d.Description);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(d.I18n);
            if (doc.RootElement.TryGetProperty(lang.ToLowerInvariant(), out var t))
            {
                var name = t.TryGetProperty("name", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String ? n.GetString()! : d.Name;
                var desc = t.TryGetProperty("description", out var ds) && ds.ValueKind == System.Text.Json.JsonValueKind.String ? ds.GetString() : d.Description;
                return (name, desc);
            }
        }
        catch { /* malformed i18n JSON -> fall back to base */ }
        return (d.Name, d.Description);
    }

    // GET /api/directory/map?category=&country= - companies that have coordinates, as lightweight
    // points for the map view (blueprint: geo layer). Capped - the map clusters client-side.
    [HttpGet("map")]
    [AllowAnonymous]
    public async Task<IActionResult> Map([FromQuery] string? category, [FromQuery] string? country)
    {
        var query = _db.DirectoryCompanies.AsNoTracking()
            .Where(d => d.Status != "closed" && d.Latitude != null && d.Longitude != null);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(d => d.Category == category);
        if (!string.IsNullOrWhiteSpace(country)) query = query.Where(d => d.CountryCode == country);

        var points = await query.Take(2000)
            .Select(d => new DirectoryMapPointDto
            {
                PublicId = d.PublicId, Slug = d.Slug, Name = d.Name, Category = d.Category,
                City = d.City, Latitude = d.Latitude!.Value, Longitude = d.Longitude!.Value,
            }).ToListAsync();
        return Ok(points);
    }

    // GET /api/directory/{slug}/listings - adverts published by this company. The graph edge
    // Firma -> Ogłoszenia (blueprint section 05): only resolvable for a company linked to a Carizo
    // account (PartnerId set), via Partner.LinkedUserId -> that user's active adverts.
    [HttpGet("{slug}/listings")]
    [AllowAnonymous]
    public async Task<IActionResult> Listings(string slug, [FromQuery] int page = 1, [FromQuery] int pageSize = 12)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 24);

        var company = await _db.DirectoryCompanies.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Slug == slug && d.Status != "closed");
        if (company == null) return NotFound();
        if (company.PartnerId == null)
            return Ok(new DirectoryListingsResultDto { Linked = false });

        var linkedUserId = await _db.Partners.AsNoTracking()
            .Where(p => p.Id == company.PartnerId)
            .Select(p => (int?)p.LinkedUserId).FirstOrDefaultAsync();
        if (linkedUserId == null)
            return Ok(new DirectoryListingsResultDto { Linked = false });

        var now = DateTime.UtcNow;
        var q = _db.CarAdverts.AsNoTracking()
            .Where(a => a.UserId == linkedUserId && a.IsActive && !a.IsHidden && (a.ExpiresAt == null || a.ExpiresAt > now));

        var total = await q.CountAsync();
        var items = await q
            .OrderByDescending(a => a.FeaturedUntil != null && a.FeaturedUntil > now)
            .ThenByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new DirectoryListingCardDto
            {
                Id = a.Id, Slug = a.Slug, Title = a.Title, Price = a.Price, Currency = a.Currency,
                Year = a.Year, Mileage = a.Mileage,
                BrandName = a.Brand != null ? a.Brand.Name : null,
                ModelName = a.Model != null ? a.Model.Name : null,
                ImageUrl = a.Images.Where(i => i.IsMain).Select(i => i.Url).FirstOrDefault()
                           ?? a.Images.OrderBy(i => i.Order).Select(i => i.Url).FirstOrDefault(),
                Badge = a.Badge,
            })
            .ToListAsync();

        return Ok(new DirectoryListingsResultDto { Items = items, Total = total, Linked = true });
    }

    // ── Admin ─────────────────────────────────────────────────────────────
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] DirectoryCompanyInputDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Category))
            return BadRequest("Nazwa i kategoria są wymagane.");

        var company = new DirectoryCompany
        {
            PublicId = CarizoId.New("org", dto.CountryCode),
            Name = dto.Name.Trim(),
            NameNormalized = CarizoId.Normalize(dto.Name),
            Category = dto.Category.Trim(),
            CountryCode = dto.CountryCode?.Trim().ToUpperInvariant(),
            City = dto.City?.Trim(), Address = dto.Address?.Trim(), PostalCode = dto.PostalCode?.Trim(),
            Phone = dto.Phone?.Trim(), Email = dto.Email?.Trim(), Website = dto.Website?.Trim(),
            ProfileUrl = dto.ProfileUrl?.Trim(), Language = dto.Language?.Trim(),
            Description = dto.Description?.Trim(),
            Latitude = dto.Latitude, Longitude = dto.Longitude,
            I18n = SerializeI18n(dto.I18n),
            Status = string.IsNullOrWhiteSpace(dto.Status) ? "active" : dto.Status.Trim(),
            Source = "admin",
            Slug = await UniqueSlugAsync(dto.Name, dto.City),
        };
        _db.DirectoryCompanies.Add(company);
        await _db.SaveChangesAsync();
        return Ok(new { company.PublicId, company.Slug });
    }

    [HttpPut("{slug}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(string slug, [FromBody] DirectoryCompanyInputDto dto)
    {
        var d = await _db.DirectoryCompanies.FirstOrDefaultAsync(x => x.Slug == slug);
        if (d == null) return NotFound();

        d.Name = dto.Name.Trim();
        d.NameNormalized = CarizoId.Normalize(dto.Name);
        d.Category = dto.Category.Trim();
        d.CountryCode = dto.CountryCode?.Trim().ToUpperInvariant();
        d.City = dto.City?.Trim(); d.Address = dto.Address?.Trim(); d.PostalCode = dto.PostalCode?.Trim();
        d.Phone = dto.Phone?.Trim(); d.Email = dto.Email?.Trim(); d.Website = dto.Website?.Trim();
        d.ProfileUrl = dto.ProfileUrl?.Trim(); d.Language = dto.Language?.Trim();
        d.Description = dto.Description?.Trim();
        if (dto.Latitude != null) d.Latitude = dto.Latitude;
        if (dto.Longitude != null) d.Longitude = dto.Longitude;
        if (dto.I18n != null) d.I18n = SerializeI18n(dto.I18n);
        if (!string.IsNullOrWhiteSpace(dto.Status)) d.Status = dto.Status.Trim();
        d.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { d.PublicId, d.Slug });
    }

    // POST /api/directory/{slug}/translate?langs=en,de,fr - on-demand auto-translation of one
    // company (admin "Auto-tłumacz" button). Fills only the languages that are still missing.
    // Returns 400 with a clear message if no translation provider is configured.
    [HttpPost("{slug}/translate")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Translate(string slug, [FromQuery] string? langs)
    {
        if (!_translator.IsConfigured)
            return BadRequest("Silnik tłumaczeń nie jest skonfigurowany. Ustaw TRANSLATION_API_KEY w środowisku.");

        var d = await _db.DirectoryCompanies.FirstOrDefaultAsync(x => x.Slug == slug);
        if (d == null) return NotFound();

        var targets = (string.IsNullOrWhiteSpace(langs) ? "en,de,fr" : langs)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant()).Distinct().ToArray();
        var baseLang = string.IsNullOrWhiteSpace(d.Language) ? "pl" : d.Language.ToLowerInvariant();

        var existing = new Dictionary<string, LocalizedTextDto>();
        if (!string.IsNullOrWhiteSpace(d.I18n))
        {
            try { existing = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, LocalizedTextDto>>(d.I18n) ?? new(); }
            catch { existing = new(); }
        }

        var filled = new List<string>();
        foreach (var lang in targets)
        {
            if (lang == baseLang || existing.ContainsKey(lang)) continue;
            var name = await _translator.TranslateAsync(d.Name, lang, baseLang, HttpContext.RequestAborted);
            string? desc = string.IsNullOrWhiteSpace(d.Description) ? null
                : await _translator.TranslateAsync(d.Description!, lang, baseLang, HttpContext.RequestAborted);
            if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(desc))
            {
                existing[lang] = new LocalizedTextDto { Name = name, Description = desc };
                filled.Add(lang);
            }
        }

        if (filled.Count > 0)
        {
            d.I18n = System.Text.Json.JsonSerializer.Serialize(existing);
            d.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return Ok(new { filled, languages = existing.Keys.ToList() });
    }

    [HttpDelete("{slug}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(string slug)
    {
        var d = await _db.DirectoryCompanies.FirstOrDefaultAsync(x => x.Slug == slug);
        if (d == null) return NotFound();
        // Soft-close rather than hard-delete: keeps the Carizo ID stable if the row is re-seeded.
        d.Status = "closed";
        d.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // POST /api/directory/import - first ingestion connector (blueprint section 10). Takes a batch
    // of rows (parsed client-side from CSV/JSON, or later pushed by a source connector) and upserts
    // them into the directory, deduplicating by normalized name. Idempotent: re-importing the same
    // batch updates gaps instead of creating duplicates.
    [HttpPost("import")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Import([FromBody] DirectoryImportRequestDto req)
    {
        if (req.Rows == null || req.Rows.Count == 0)
            return BadRequest("Brak wierszy do importu.");
        if (req.Rows.Count > 5000)
            return BadRequest("Maksymalnie 5000 wierszy na jeden import.");

        var result = new DirectoryImportResultDto { Received = req.Rows.Count };
        var source = string.IsNullOrWhiteSpace(req.Source) ? "import:manual" : req.Source!.Trim();
        var defaultCategory = string.IsNullOrWhiteSpace(req.DefaultCategory) ? "firmy" : req.DefaultCategory!.Trim();

        // Index existing rows by normalized name once, up front, for O(1) dedup across the batch.
        var existing = (await _db.DirectoryCompanies.ToListAsync())
            .GroupBy(d => d.NameNormalized)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var takenSlugs = new HashSet<string>(existing.Values.Select(e => e.Slug), StringComparer.OrdinalIgnoreCase);

        foreach (var row in req.Rows)
        {
            if (string.IsNullOrWhiteSpace(row.Name)) { result.Skipped++; continue; }
            var norm = CarizoId.Normalize(row.Name);
            if (norm.Length == 0) { result.Skipped++; continue; }

            if (existing.TryGetValue(norm, out var d))
            {
                // Fill only empty fields - never overwrite a curated/verified value with import data.
                bool changed = false;
                if (string.IsNullOrEmpty(d.City) && !string.IsNullOrWhiteSpace(row.City)) { d.City = row.City!.Trim(); changed = true; }
                if (string.IsNullOrEmpty(d.Address) && !string.IsNullOrWhiteSpace(row.Address)) { d.Address = row.Address!.Trim(); changed = true; }
                if (string.IsNullOrEmpty(d.PostalCode) && !string.IsNullOrWhiteSpace(row.PostalCode)) { d.PostalCode = row.PostalCode!.Trim(); changed = true; }
                if (string.IsNullOrEmpty(d.Phone) && !string.IsNullOrWhiteSpace(row.Phone)) { d.Phone = row.Phone!.Trim(); changed = true; }
                if (string.IsNullOrEmpty(d.Email) && !string.IsNullOrWhiteSpace(row.Email)) { d.Email = row.Email!.Trim(); d.EmailType = ClassifyEmail(row.Email); changed = true; }
                if (string.IsNullOrEmpty(d.Website) && !string.IsNullOrWhiteSpace(row.Website)) { d.Website = row.Website!.Trim(); changed = true; }
                if (changed) { d.UpdatedAt = DateTime.UtcNow; result.Updated++; } else { result.Skipped++; }
                continue;
            }

            var slugBase = CarizoId.Slugify(row.Name);
            var slug = slugBase;
            if (takenSlugs.Contains(slug))
                slug = $"{slugBase}-{Guid.NewGuid().ToString("N")[..6]}";
            takenSlugs.Add(slug);

            var company = new DirectoryCompany
            {
                PublicId = CarizoId.New("org", row.CountryCode),
                Name = row.Name.Trim(),
                NameNormalized = norm,
                Category = string.IsNullOrWhiteSpace(row.Category) ? defaultCategory : row.Category!.Trim(),
                CountryCode = string.IsNullOrWhiteSpace(row.CountryCode) ? null : row.CountryCode!.Trim().ToUpperInvariant(),
                City = row.City?.Trim(), Address = row.Address?.Trim(), PostalCode = row.PostalCode?.Trim(),
                Phone = row.Phone?.Trim(), Email = row.Email?.Trim(), EmailType = ClassifyEmail(row.Email),
                Website = row.Website?.Trim(), Latitude = row.Latitude, Longitude = row.Longitude,
                // Imported rows are unverified until confirmed - they came from an external source.
                Status = "unverified",
                Source = source,
                Slug = slug,
            };
            _db.DirectoryCompanies.Add(company);
            existing[norm] = company;
            result.Created++;
        }

        await _db.SaveChangesAsync();
        result.Notes.Add($"Import zakończony: {result.Created} nowych, {result.Updated} uzupełnionych, {result.Skipped} pominiętych.");
        return Ok(result);
    }

    // Serializes the admin's per-language translations to the I18n column, dropping empty entries.
    // Returns null when there's nothing to store (so the column stays clean).
    private static string? SerializeI18n(Dictionary<string, LocalizedTextDto>? i18n)
    {
        if (i18n == null || i18n.Count == 0) return null;
        var cleaned = i18n
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key)
                         && (!string.IsNullOrWhiteSpace(kv.Value?.Name) || !string.IsNullOrWhiteSpace(kv.Value?.Description)))
            .ToDictionary(
                kv => kv.Key.Trim().ToLowerInvariant(),
                kv => new LocalizedTextDto { Name = kv.Value!.Name?.Trim(), Description = kv.Value.Description?.Trim() });
        return cleaned.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(cleaned);
    }

    private static string? ClassifyEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) return null;
        var local = email.Split('@')[0].ToLowerInvariant();
        string[] role = { "biuro", "office", "kontakt", "contact", "info", "sprzedaz", "sales", "sekretariat", "handel" };
        return role.Any(r => local.Contains(r)) ? "role" : "personal";
    }

    private async Task<string> UniqueSlugAsync(string name, string? city)
    {
        var baseSlug = CarizoId.Slugify(name);
        if (!await _db.DirectoryCompanies.AnyAsync(x => x.Slug == baseSlug)) return baseSlug;
        var withCity = $"{baseSlug}-{CarizoId.Slugify(city)}";
        if (!string.IsNullOrWhiteSpace(city) && !await _db.DirectoryCompanies.AnyAsync(x => x.Slug == withCity))
            return withCity;
        // Last resort: append a short token so the unique index never rejects the insert.
        return $"{baseSlug}-{Guid.NewGuid().ToString("N")[..6]}";
    }
}
