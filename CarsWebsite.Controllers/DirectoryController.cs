using cars_website_api.CarsWebsite.DTOs.Directory;
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

    public DirectoryController(AppDbContext db) => _db = db;

    // GET /api/directory?q=&category=&country=&city=&page=1&pageSize=24
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> List(
        [FromQuery] string? q, [FromQuery] string? category, [FromQuery] string? country,
        [FromQuery] string? city, [FromQuery] int page = 1, [FromQuery] int pageSize = 24)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 60);

        // Only surface rows that aren't closed. Seeded 'unverified' rows are still shown - the
        // directory is useful before every entry is hand-confirmed - but flagged in the UI.
        var query = _db.DirectoryCompanies.AsNoTracking().Where(d => d.Status != "closed");

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

    // GET /api/directory/{slug}
    [HttpGet("{slug}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetBySlug(string slug)
    {
        var d = await _db.DirectoryCompanies.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug && x.Status != "closed");
        if (d == null) return NotFound();
        return Ok(new DirectoryCompanyDetailDto
        {
            PublicId = d.PublicId, Slug = d.Slug, Name = d.Name, Category = d.Category,
            CountryCode = d.CountryCode, City = d.City, Address = d.Address, PostalCode = d.PostalCode,
            Phone = d.Phone, Email = d.Email, Website = d.Website, ProfileUrl = d.ProfileUrl,
            Language = d.Language, Latitude = d.Latitude, Longitude = d.Longitude,
            Status = d.Status, CreatedAt = d.CreatedAt, UpdatedAt = d.UpdatedAt,
        });
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
            Status = string.IsNullOrWhiteSpace(dto.Status) ? "active" : dto.Status.Trim(),
            Source = "admin",
            Slug = await UniqueSlugAsync(dto.Name, dto.City),
        };
        _db.DirectoryCompanies.Add(company);
        await _db.SaveChangesAsync();
        return Ok(new { company.PublicId, company.Slug });
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(int id, [FromBody] DirectoryCompanyInputDto dto)
    {
        var d = await _db.DirectoryCompanies.FirstOrDefaultAsync(x => x.Id == id);
        if (d == null) return NotFound();

        d.Name = dto.Name.Trim();
        d.NameNormalized = CarizoId.Normalize(dto.Name);
        d.Category = dto.Category.Trim();
        d.CountryCode = dto.CountryCode?.Trim().ToUpperInvariant();
        d.City = dto.City?.Trim(); d.Address = dto.Address?.Trim(); d.PostalCode = dto.PostalCode?.Trim();
        d.Phone = dto.Phone?.Trim(); d.Email = dto.Email?.Trim(); d.Website = dto.Website?.Trim();
        d.ProfileUrl = dto.ProfileUrl?.Trim(); d.Language = dto.Language?.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Status)) d.Status = dto.Status.Trim();
        d.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { d.PublicId, d.Slug });
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(int id)
    {
        var d = await _db.DirectoryCompanies.FirstOrDefaultAsync(x => x.Id == id);
        if (d == null) return NotFound();
        // Soft-close rather than hard-delete: keeps the Carizo ID stable if the row is re-seeded.
        d.Status = "closed";
        d.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
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
