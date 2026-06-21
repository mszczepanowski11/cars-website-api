using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.DTOs.Admin;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;
    private readonly AppDbContext _db;

    public AdminController(IAdminService adminService, AppDbContext db)
    {
        _adminService = adminService;
        _db = db;
    }

    private int GetUserId()
    {
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
        return userId;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
        => Ok(await _adminService.GetStatsAsync());

    [HttpGet("reports")]
    public async Task<IActionResult> GetReports([FromQuery] AdminReportFilterDto filter)
        => Ok(await _adminService.GetReportsAsync(filter));

    [HttpGet("reports/{id}")]
    public async Task<IActionResult> GetReport(int id)
    {
        var report = await _adminService.GetReportByIdAsync(id);
        if (report == null) return NotFound();
        return Ok(report);
    }

    [HttpPost("reports/{id}/resolve")]
    public async Task<IActionResult> ResolveReport(int id, [FromBody] AdminResolveReportDto dto)
    {
        await _adminService.ResolveReportAsync(id, GetUserId(), dto.Note);
        return NoContent();
    }

    [HttpPost("reports/{id}/reject")]
    public async Task<IActionResult> RejectReport(int id, [FromBody] AdminResolveReportDto dto)
    {
        await _adminService.RejectReportAsync(id, GetUserId(), dto.Note);
        return NoContent();
    }

    [HttpPost("adverts/{id}/hide")]
    public async Task<IActionResult> HideAdvert(int id, [FromBody] AdminActionRequestDto dto)
    {
        await _adminService.HideAdvertAsync(id, GetUserId(), dto.Note);
        return NoContent();
    }

    [HttpPost("adverts/{id}/unhide")]
    public async Task<IActionResult> UnhideAdvert(int id)
    {
        await _adminService.UnhideAdvertAsync(id, GetUserId());
        return NoContent();
    }

    [HttpDelete("adverts/{id}")]
    public async Task<IActionResult> DeleteAdvert(int id)
    {
        await _adminService.DeleteAdvertAsync(id, GetUserId(), null);
        return NoContent();
    }

    [HttpPost("adverts/{id}/activate")]
    public async Task<IActionResult> ActivateAdvert(int id)
    {
        await _adminService.ActivateAdvertAsync(id, GetUserId());
        return NoContent();
    }

    [HttpPost("adverts/{id}/deactivate")]
    public async Task<IActionResult> DeactivateAdvert(int id)
    {
        await _adminService.DeactivateAdvertAsync(id, GetUserId());
        return NoContent();
    }

    [HttpPost("users/{id}/block")]
    public async Task<IActionResult> BlockUser(int id, [FromBody] AdminBlockUserDto dto)
    {
        await _adminService.BlockUserAsync(id, GetUserId(), dto.Reason);
        return NoContent();
    }

    [HttpPost("users/{id}/unblock")]
    public async Task<IActionResult> UnblockUser(int id)
    {
        await _adminService.UnblockUserAsync(id, GetUserId());
        return NoContent();
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        await _adminService.DeleteUserAsync(id, GetUserId(), "Usunięte przez administratora");
        return NoContent();
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _adminService.GetUsersAsync(search, page, pageSize));

    [HttpGet("adverts")]
    public async Task<IActionResult> GetAdverts(
        [FromQuery] string? search,
        [FromQuery] bool? isHidden,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
        => Ok(await _adminService.GetAdvertsAsync(search, isHidden, isActive, page, pageSize));

    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await _adminService.GetActionLogsAsync(page, pageSize));

    // ── Taxonomy management ────────────────────────────────────────────────────

    [HttpGet("features")]
    public async Task<IActionResult> GetFeatures([FromQuery] string? search, [FromQuery] int? categoryId)
    {
        var q = _db.Features.Include(f => f.Category).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(f => f.Name.Contains(search));
        if (categoryId.HasValue)
            q = q.Where(f => f.CategoryId == categoryId.Value);
        var items = await q.OrderBy(f => f.Category.Name).ThenBy(f => f.Name)
            .Select(f => new { f.Id, f.Name, Category = new { f.Category.Id, f.Category.Name, f.Category.VehicleCategoryId } })
            .ToListAsync();
        return Ok(items);
    }

    [HttpPost("features")]
    public async Task<IActionResult> CreateFeature([FromBody] CreateFeatureDto dto)
    {
        var cat = await _db.FeatureCategories.FindAsync(dto.CategoryId);
        if (cat == null) return BadRequest("Kategoria wyposażenia nie istnieje.");
        var feature = new Feature { Name = dto.Name, CategoryId = dto.CategoryId };
        _db.Features.Add(feature);
        await _db.SaveChangesAsync();
        return Ok(new { feature.Id, feature.Name });
    }

    [HttpDelete("features/{id}")]
    public async Task<IActionResult> DeleteFeature(int id)
    {
        var feature = await _db.Features.FindAsync(id);
        if (feature == null) return NotFound();
        _db.Features.Remove(feature);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("feature-categories")]
    public async Task<IActionResult> GetFeatureCategories()
    {
        var items = await _db.FeatureCategories
            .Include(fc => fc.VehicleCategory)
            .Include(fc => fc.Brand)
            .Include(fc => fc.Model)
            .OrderBy(fc => fc.Name)
            .Select(fc => new {
                fc.Id, fc.Name, fc.VehicleCategoryId,
                VehicleCategoryName = fc.VehicleCategory != null ? fc.VehicleCategory.Name : null,
                fc.BrandId, BrandName = fc.Brand != null ? fc.Brand.Name : null,
                fc.ModelId, ModelName = fc.Model != null ? fc.Model.Name : null,
            })
            .ToListAsync();
        return Ok(items);
    }

    [HttpPost("feature-categories")]
    public async Task<IActionResult> CreateFeatureCategory([FromBody] CreateFeatureCategoryDto dto)
    {
        var cat = new FeatureCategory {
            Name = dto.Name,
            VehicleCategoryId = dto.VehicleCategoryId,
            BrandId = dto.BrandId,
            ModelId = dto.ModelId,
        };
        _db.FeatureCategories.Add(cat);
        await _db.SaveChangesAsync();
        return Ok(new { cat.Id, cat.Name });
    }

    [HttpDelete("feature-categories/{id}")]
    public async Task<IActionResult> DeleteFeatureCategory(int id)
    {
        var cat = await _db.FeatureCategories.FindAsync(id);
        if (cat == null) return NotFound();
        _db.FeatureCategories.Remove(cat);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Brand CRUD ─────────────────────────────────────────────────────────────

    [HttpGet("brands")]
    public async Task<IActionResult> GetAdminBrands([FromQuery] string? search, [FromQuery] int? categoryId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var q = _db.Brands.Include(b => b.Categories).Include(b => b.Models).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(b => b.Name.Contains(search) || b.Slug.Contains(search));
        if (categoryId.HasValue)
            q = q.Where(b => b.Categories.Any(c => c.Id == categoryId.Value));
        var total = await q.CountAsync();
        var items = await q.OrderBy(b => b.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(b => new {
                b.Id, b.Name, b.Slug,
                Categories = b.Categories.Select(c => new { c.Id, c.Name }).ToList(),
                ModelCount = b.Models.Count()
            }).ToListAsync();
        return Ok(new { items, total, page, pageSize });
    }

    [HttpPost("brands")]
    public async Task<IActionResult> CreateAdminBrand([FromBody] CreateBrandDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Nazwa marki jest wymagana.");
        var slug = (dto.Slug ?? dto.Name.ToLowerInvariant()
            .Replace(" ", "-").Replace("ę","e").Replace("ą","a").Replace("ó","o")
            .Replace("ś","s").Replace("ł","l").Replace("ź","z").Replace("ż","z")
            .Replace("ć","c").Replace("ń","n"));
        if (await _db.Brands.AnyAsync(b => b.Slug == slug))
            return BadRequest($"Marka ze slugiem '{slug}' już istnieje.");
        var cats = dto.CategoryIds?.Count > 0
            ? await _db.VehicleCategories.Where(c => dto.CategoryIds.Contains(c.Id)).ToListAsync()
            : new List<VehicleCategory>();
        var brand = new Brand { Name = dto.Name, Slug = slug, Categories = cats };
        _db.Brands.Add(brand);
        await _db.SaveChangesAsync();
        return Ok(new { brand.Id, brand.Name, brand.Slug });
    }

    [HttpPut("brands/{id}")]
    public async Task<IActionResult> UpdateAdminBrand(int id, [FromBody] CreateBrandDto dto)
    {
        var brand = await _db.Brands.Include(b => b.Categories).FirstOrDefaultAsync(b => b.Id == id);
        if (brand == null) return NotFound();
        brand.Name = dto.Name;
        if (!string.IsNullOrWhiteSpace(dto.Slug)) brand.Slug = dto.Slug;
        if (dto.CategoryIds != null)
            brand.Categories = await _db.VehicleCategories.Where(c => dto.CategoryIds.Contains(c.Id)).ToListAsync();
        await _db.SaveChangesAsync();
        return Ok(new { brand.Id, brand.Name, brand.Slug });
    }

    [HttpDelete("brands/{id}")]
    public async Task<IActionResult> DeleteAdminBrand(int id)
    {
        var brand = await _db.Brands.Include(b => b.Models).FirstOrDefaultAsync(b => b.Id == id);
        if (brand == null) return NotFound();
        if (brand.Models.Any()) return BadRequest("Nie można usunąć marki posiadającej modele. Najpierw usuń modele.");
        _db.Brands.Remove(brand);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Model CRUD ─────────────────────────────────────────────────────────────

    [HttpGet("models")]
    public async Task<IActionResult> GetAdminModels([FromQuery] int? brandId, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var q = _db.Models.Include(m => m.Brand).Include(m => m.Generations).AsQueryable();
        if (brandId.HasValue) q = q.Where(m => m.BrandId == brandId.Value);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(m => m.Name.Contains(search));
        var total = await q.CountAsync();
        var items = await q.OrderBy(m => m.Brand.Name).ThenBy(m => m.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new { m.Id, m.Name, m.Slug, m.BrandId, BrandName = m.Brand.Name, GenerationCount = m.Generations.Count() })
            .ToListAsync();
        return Ok(new { items, total, page, pageSize });
    }

    [HttpPost("models")]
    public async Task<IActionResult> CreateAdminModel([FromBody] CreateModelDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || dto.BrandId <= 0) return BadRequest("Nazwa i marka są wymagane.");
        var brand = await _db.Brands.FindAsync(dto.BrandId);
        if (brand == null) return BadRequest("Marka nie istnieje.");
        var slug = dto.Slug ?? $"{brand.Slug}-{dto.Name.ToLowerInvariant().Replace(" ", "-")}";
        var model = new Model { BrandId = dto.BrandId, Name = dto.Name, Slug = slug };
        _db.Models.Add(model);
        await _db.SaveChangesAsync();
        return Ok(new { model.Id, model.Name, model.Slug, model.BrandId });
    }

    [HttpPut("models/{id}")]
    public async Task<IActionResult> UpdateAdminModel(int id, [FromBody] CreateModelDto dto)
    {
        var model = await _db.Models.FindAsync(id);
        if (model == null) return NotFound();
        model.Name = dto.Name;
        if (!string.IsNullOrWhiteSpace(dto.Slug)) model.Slug = dto.Slug;
        if (dto.BrandId > 0) model.BrandId = dto.BrandId;
        await _db.SaveChangesAsync();
        return Ok(new { model.Id, model.Name, model.Slug });
    }

    [HttpDelete("models/{id}")]
    public async Task<IActionResult> DeleteAdminModel(int id)
    {
        var model = await _db.Models.Include(m => m.Generations).FirstOrDefaultAsync(m => m.Id == id);
        if (model == null) return NotFound();
        if (model.Generations.Any()) return BadRequest("Nie można usunąć modelu posiadającego generacje. Najpierw usuń generacje.");
        _db.Models.Remove(model);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Generation CRUD ────────────────────────────────────────────────────────

    [HttpGet("generations")]
    public async Task<IActionResult> GetAdminGenerations([FromQuery] int? modelId, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var q = _db.Generations.Include(g => g.Model).ThenInclude(m => m.Brand).Include(g => g.EngineVersions).AsQueryable();
        if (modelId.HasValue) q = q.Where(g => g.ModelId == modelId.Value);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(g => g.Name.Contains(search));
        var total = await q.CountAsync();
        var items = await q.OrderBy(g => g.Model.Brand.Name).ThenBy(g => g.Model.Name).ThenBy(g => g.YearFrom)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(g => new { g.Id, g.Name, g.Slug, g.YearFrom, g.YearTo, g.ModelId, ModelName = g.Model.Name, BrandName = g.Model.Brand.Name, EngineCount = g.EngineVersions.Count() })
            .ToListAsync();
        return Ok(new { items, total, page, pageSize });
    }

    [HttpPost("generations")]
    public async Task<IActionResult> CreateAdminGeneration([FromBody] CreateGenerationDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || dto.ModelId <= 0) return BadRequest("Nazwa i model są wymagane.");
        var model = await _db.Models.Include(m => m.Brand).FirstOrDefaultAsync(m => m.Id == dto.ModelId);
        if (model == null) return BadRequest("Model nie istnieje.");
        var slug = dto.Slug ?? $"{model.Slug}-{dto.YearFrom}";
        var gen = new Generation { ModelId = dto.ModelId, Name = dto.Name, Slug = slug, YearFrom = dto.YearFrom, YearTo = dto.YearTo };
        _db.Generations.Add(gen);
        await _db.SaveChangesAsync();
        return Ok(new { gen.Id, gen.Name, gen.Slug, gen.ModelId });
    }

    [HttpPut("generations/{id}")]
    public async Task<IActionResult> UpdateAdminGeneration(int id, [FromBody] CreateGenerationDto dto)
    {
        var gen = await _db.Generations.FindAsync(id);
        if (gen == null) return NotFound();
        gen.Name = dto.Name;
        gen.YearFrom = dto.YearFrom;
        gen.YearTo = dto.YearTo;
        if (!string.IsNullOrWhiteSpace(dto.Slug)) gen.Slug = dto.Slug;
        if (dto.ModelId > 0) gen.ModelId = dto.ModelId;
        await _db.SaveChangesAsync();
        return Ok(new { gen.Id, gen.Name, gen.Slug });
    }

    [HttpDelete("generations/{id}")]
    public async Task<IActionResult> DeleteAdminGeneration(int id)
    {
        var gen = await _db.Generations.Include(g => g.EngineVersions).FirstOrDefaultAsync(g => g.Id == id);
        if (gen == null) return NotFound();
        if (gen.EngineVersions.Any()) return BadRequest("Nie można usunąć generacji posiadającej wersje silników. Najpierw usuń silniki.");
        _db.Generations.Remove(gen);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Engine CRUD ────────────────────────────────────────────────────────────

    [HttpGet("engines")]
    public async Task<IActionResult> GetAdminEngines([FromQuery] int? generationId, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var q = _db.EngineVersions
            .Include(e => e.Generation).ThenInclude(g => g.Model).ThenInclude(m => m.Brand)
            .Include(e => e.FuelType).AsQueryable();
        if (generationId.HasValue) q = q.Where(e => e.GenerationId == generationId.Value);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(e => e.EngineName.Contains(search));
        var total = await q.CountAsync();
        var items = await q.OrderBy(e => e.Generation.Model.Brand.Name).ThenBy(e => e.EngineName)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new {
                e.Id, e.EngineName, e.PowerHP, e.PowerKW, e.Displacement,
                e.FuelTypeId, FuelTypeName = e.FuelType.Name,
                e.GenerationId, GenerationName = e.Generation.Name,
                ModelName = e.Generation.Model.Name, BrandName = e.Generation.Model.Brand.Name,
                e.FuelConsumptionCity, e.FuelConsumptionHighway, e.FuelConsumptionCombined
            }).ToListAsync();
        return Ok(new { items, total, page, pageSize });
    }

    [HttpPost("engines")]
    public async Task<IActionResult> CreateAdminEngine([FromBody] CreateEngineDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.EngineName) || dto.GenerationId <= 0)
            return BadRequest("Nazwa silnika i generacja są wymagane.");
        if (!await _db.Generations.AnyAsync(g => g.Id == dto.GenerationId))
            return BadRequest("Generacja nie istnieje.");
        if (!await _db.FuelTypes.AnyAsync(f => f.Id == dto.FuelTypeId))
            return BadRequest("Rodzaj paliwa nie istnieje.");
        var engine = new EngineVersion {
            GenerationId = dto.GenerationId, EngineName = dto.EngineName, FuelTypeId = dto.FuelTypeId,
            PowerHP = dto.PowerHP, PowerKW = dto.PowerKW, Displacement = dto.Displacement,
            FuelConsumptionCity = dto.FuelConsumptionCity,
            FuelConsumptionHighway = dto.FuelConsumptionHighway,
            FuelConsumptionCombined = dto.FuelConsumptionCombined
        };
        _db.EngineVersions.Add(engine);
        await _db.SaveChangesAsync();
        return Ok(new { engine.Id, engine.EngineName, engine.GenerationId });
    }

    [HttpPut("engines/{id}")]
    public async Task<IActionResult> UpdateAdminEngine(int id, [FromBody] CreateEngineDto dto)
    {
        var engine = await _db.EngineVersions.FindAsync(id);
        if (engine == null) return NotFound();
        engine.EngineName = dto.EngineName;
        engine.FuelTypeId = dto.FuelTypeId;
        engine.PowerHP = dto.PowerHP;
        engine.PowerKW = dto.PowerKW;
        engine.Displacement = dto.Displacement;
        engine.FuelConsumptionCity = dto.FuelConsumptionCity;
        engine.FuelConsumptionHighway = dto.FuelConsumptionHighway;
        engine.FuelConsumptionCombined = dto.FuelConsumptionCombined;
        await _db.SaveChangesAsync();
        return Ok(new { engine.Id, engine.EngineName });
    }

    [HttpDelete("engines/{id}")]
    public async Task<IActionResult> DeleteAdminEngine(int id)
    {
        var engine = await _db.EngineVersions.FindAsync(id);
        if (engine == null) return NotFound();
        _db.EngineVersions.Remove(engine);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}