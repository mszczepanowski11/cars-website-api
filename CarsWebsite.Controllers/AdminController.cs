using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.DTOs.Admin;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public AdminController(IAdminService adminService, AppDbContext db, IConfiguration config)
    {
        _adminService = adminService;
        _db = db;
        _config = config;
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
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? search,
        [FromQuery] string? accountType,
        [FromQuery] bool? isBlocked,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
        => Ok(await _adminService.GetUsersAsync(search, accountType, isBlocked, page, pageSize));

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
        if (cat == null) return BadRequest(new { message = "Kategoria wyposażenia nie istnieje." });
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

    // ── Custom category requests ──────────────────────────────────────────────

    [HttpGet("custom-categories")]
    public async Task<IActionResult> GetCustomCategories([FromQuery] string? status = null)
    {
        var query = _db.CustomCategoryRequests.AsQueryable();
        if (!string.IsNullOrEmpty(status))
            query = query.Where(r => r.Status == status);
        var results = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
        return Ok(results);
    }

    [HttpPut("custom-categories/{id}/approve")]
    public async Task<IActionResult> ApproveCustomCategory(int id, [FromBody] AdminReviewCustomCategoryDto dto)
    {
        var request = await _db.CustomCategoryRequests.FindAsync(id);
        if (request == null) return NotFound();
        request.Status = "Approved";
        request.AdminNotes = dto.Notes;
        request.ReviewedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(request);
    }

    [HttpPut("custom-categories/{id}/reject")]
    public async Task<IActionResult> RejectCustomCategory(int id, [FromBody] AdminReviewCustomCategoryDto dto)
    {
        var request = await _db.CustomCategoryRequests.FindAsync(id);
        if (request == null) return NotFound();
        request.Status = "Rejected";
        request.AdminNotes = dto.Notes;
        request.ReviewedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(request);
    }

    // ── DB cleanup endpoints ───────────────────────────────────────────────────

    [HttpGet("suspicious-records")]
    public async Task<IActionResult> GetSuspiciousRecords()
    {
        var testPatterns = new[] { "test", "asdf", "qwer", "aaaa", "bbbb", "xxx", "sample", "lorem" };
        var suspicious = await _db.CarAdverts
            .Where(a => testPatterns.Any(p => a.Title != null && a.Title.ToLower().Contains(p)))
            .Select(a => new { a.Id, a.Title, a.CreatedAt })
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .ToListAsync();
        return Ok(suspicious);
    }

    [HttpDelete("suspicious-records/{id}")]
    public async Task<IActionResult> DeleteSuspiciousRecord(int id)
    {
        var advert = await _db.CarAdverts.FindAsync(id);
        if (advert == null) return NotFound();
        _db.CarAdverts.Remove(advert);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Deleted", id });
    }

    // ── Quality report ────────────────────────────────────────────────────────

    [HttpGet("quality-report")]
    public async Task<IActionResult> GetQualityReport()
    {
        var brandsWithoutModels = await _db.Brands
            .Where(b => !b.Models.Any())
            .Select(b => new { b.Id, b.Name })
            .OrderBy(b => b.Name)
            .ToListAsync();

        var modelsWithoutGenerations = await _db.Models
            .Include(m => m.Brand)
            .Where(m => !m.Generations.Any())
            .Select(m => new { m.Id, m.Name, BrandName = m.Brand.Name })
            .OrderBy(m => m.BrandName).ThenBy(m => m.Name)
            .ToListAsync();

        var brandsWithoutAdverts = await _db.Brands
            .Where(b => !_db.CarAdverts.Any(ca => ca.BrandId == b.Id))
            .Select(b => new { b.Id, b.Name })
            .OrderBy(b => b.Name)
            .ToListAsync();

        var modelsWithoutAdverts = await _db.Models
            .Include(m => m.Brand)
            .Where(m => !_db.CarAdverts.Any(ca => ca.ModelId == m.Id))
            .Select(m => new { m.Id, m.Name, BrandName = m.Brand.Name })
            .OrderBy(m => m.BrandName).ThenBy(m => m.Name)
            .ToListAsync();

        var featureCategoriesEmpty = await _db.FeatureCategories
            .Where(fc => !fc.Features.Any())
            .Select(fc => new { fc.Id, fc.Name, fc.VehicleCategoryId })
            .OrderBy(fc => fc.Name)
            .ToListAsync();

        var generationsEmpty = await _db.Generations
            .Include(g => g.Model).ThenInclude(m => m.Brand)
            .Where(g => !g.EngineVersions.Any())
            .Select(g => new { g.Id, g.Name, ModelName = g.Model.Name, BrandName = g.Model.Brand.Name, g.YearFrom, g.YearTo })
            .OrderBy(g => g.BrandName).ThenBy(g => g.ModelName).ThenBy(g => g.YearFrom)
            .ToListAsync();

        var duplicateBrands = await _db.Brands
            .GroupBy(b => b.Name.ToLower())
            .Where(g => g.Count() > 1)
            .Select(g => new { Name = g.Key, Count = g.Count(), Ids = g.Select(b => b.Id).ToList() })
            .ToListAsync();

        var duplicateModels = await _db.Models
            .GroupBy(m => new { m.BrandId, NameLower = m.Name.ToLower() })
            .Where(g => g.Count() > 1)
            .Select(g => new { g.Key.BrandId, NameLower = g.Key.NameLower, Count = g.Count(), Ids = g.Select(m => m.Id).ToList() })
            .ToListAsync();

        var advertsWithBlankTitle = await _db.Adverts
            .Where(a => string.IsNullOrEmpty(a.Title))
            .Select(a => new { a.Id, a.Title, a.CreatedAt })
            .OrderByDescending(a => a.CreatedAt)
            .Take(50)
            .ToListAsync();

        var advertsNoImages = await _db.CarAdverts
            .Where(ca => ca.IsActive && !ca.IsHidden && !ca.Images.Any())
            .Select(ca => new { ca.Id, ca.Title, ca.CreatedAt })
            .OrderByDescending(ca => ca.CreatedAt)
            .Take(50)
            .ToListAsync();

        return Ok(new
        {
            summary = new
            {
                brandsWithoutModelsCount      = brandsWithoutModels.Count,
                modelsWithoutGenerationsCount = modelsWithoutGenerations.Count,
                brandsWithoutAdvertsCount     = brandsWithoutAdverts.Count,
                modelsWithoutAdvertsCount     = modelsWithoutAdverts.Count,
                emptyFeatureCategoriesCount   = featureCategoriesEmpty.Count,
                emptyGenerationsCount         = generationsEmpty.Count,
                duplicateBrandsCount          = duplicateBrands.Count,
                duplicateModelsCount          = duplicateModels.Count,
                advertsBlankTitleCount        = advertsWithBlankTitle.Count,
                advertsNoImagesCount          = advertsNoImages.Count,
            },
            brandsWithoutModels,
            modelsWithoutGenerations,
            brandsWithoutAdverts,
            modelsWithoutAdverts,
            featureCategoriesEmpty,
            generationsEmpty,
            duplicateBrands,
            duplicateModels,
            advertsWithBlankTitle,
            advertsNoImages,
        });
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

    /// <summary>
    /// Diagnostic: test SMTP connection and send a test email. Returns full error detail on failure.
    /// </summary>
    [HttpPost("test-email")]
    public async Task<IActionResult> TestEmail([FromBody] TestEmailDto dto)
    {
        var section = _config.GetSection("Smtp");
        var rawHost = (section["Host"] ?? "").Trim();
        var port    = int.TryParse(section["Port"], out var p) ? p : 587;
        var user    = section["User"] ?? "";
        var pass    = section["Password"] ?? "";
        var fromCfg = (section["From"] ?? "").Trim();
        var from    = !string.IsNullOrEmpty(fromCfg) ? fromCfg : (user.Length > 0 ? user : "test@carizo.pl");

        // Normalise host
        var host = rawHost.Contains("://") ? rawHost.Split("://", 2)[1].TrimEnd('/') : rawHost;
        if (!host.StartsWith("[") && host.Contains(':')) host = host.Split(':')[0];

        var config = new
        {
            host    = string.IsNullOrEmpty(host) ? "(not set)" : host,
            port,
            user    = string.IsNullOrEmpty(user) ? "(not set)" : user,
            pass    = string.IsNullOrEmpty(pass) ? "(not set)" : "***",
            from,
            to      = dto.To
        };

        if (string.IsNullOrEmpty(host))
            return Ok(new { success = false, error = "SMTP_HOST nie jest skonfigurowany.", config });

        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(from));
            message.To.Add(MailboxAddress.Parse(dto.To));
            message.Subject = "Test e-mail — CARIZO";
            message.Body = new TextPart("plain") { Text = $"To jest testowy e-mail CARIZO.\nNadawca: {from}\nSerwer: {host}:{port}" };

            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, SecureSocketOptions.Auto);
            if (!string.IsNullOrEmpty(user))
                await client.AuthenticateAsync(user, pass);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            return Ok(new { success = true, message = $"E-mail wysłany do {dto.To}", config });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, error = ex.Message, exceptionType = ex.GetType().Name, config });
        }
    }
}

public record TestEmailDto(string To);