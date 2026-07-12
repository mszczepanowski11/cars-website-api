using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.DTOs.Admin;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Controllers;

// Faza 2 of the category/attribute restructure (crispy-riding-mochi.md). Reads are public (the
// add-advert form and the search filters both need this without a login); writes are admin-only -
// this is the "define a new category-specific field without a deploy" surface.
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class AttributesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITaxonomyCacheVersion _taxonomyCacheVersion;

    public AttributesController(AppDbContext db, ITaxonomyCacheVersion taxonomyCacheVersion)
    {
        _db = db;
        _taxonomyCacheVersion = taxonomyCacheVersion;
    }

    // Public: GET /api/Attributes?categoryId=5&subtypeId=12&activeOnly=true
    // Returns definitions scoped to the whole category (VehicleSubtypeId == null) plus, if a
    // subtypeId is given, the ones scoped to that specific subtype - mirrors how
    // CATEGORY_CONFIGS.extraFields + SUBTYPE_EXTRA_FIELDS combine today.
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAttributeDefinitions([FromQuery] int categoryId, [FromQuery] int? subtypeId, [FromQuery] bool activeOnly = true)
    {
        var q = _db.AttributeDefinitions.AsNoTracking()
            .Where(ad => ad.VehicleCategoryId == categoryId && (ad.VehicleSubtypeId == null || ad.VehicleSubtypeId == subtypeId));
        if (activeOnly) q = q.Where(ad => ad.IsActive);

        var items = await q.OrderBy(ad => ad.SortOrder).ThenBy(ad => ad.Id)
            .Select(ad => new AttributeDefinitionDto
            {
                Id = ad.Id, VehicleCategoryId = ad.VehicleCategoryId, VehicleSubtypeId = ad.VehicleSubtypeId,
                Key = ad.Key, LabelPl = ad.LabelPl, DataType = ad.DataType, Unit = ad.Unit,
                ValidationJson = ad.ValidationJson, OptionsJson = ad.OptionsJson,
                IsRequired = ad.IsRequired, IsFilterable = ad.IsFilterable, IsSearchable = ad.IsSearchable,
                IsActive = ad.IsActive, SortOrder = ad.SortOrder,
            }).ToListAsync();
        return Ok(items);
    }

    // Admin: full list across every category, with usage counts, for the admin/attributes.vue table.
    [HttpGet("all")]
    public async Task<IActionResult> GetAllAttributeDefinitions([FromQuery] int? categoryId, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 500);
        var q = _db.AttributeDefinitions.AsNoTracking()
            .Include(ad => ad.VehicleCategory).Include(ad => ad.VehicleSubtype).AsQueryable();
        if (categoryId.HasValue) q = q.Where(ad => ad.VehicleCategoryId == categoryId.Value);

        var total = await q.CountAsync();
        var items = await q.OrderBy(ad => ad.VehicleCategoryId).ThenBy(ad => ad.SortOrder)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(ad => new AttributeDefinitionDto
            {
                Id = ad.Id, VehicleCategoryId = ad.VehicleCategoryId, VehicleCategoryName = ad.VehicleCategory.Name,
                VehicleSubtypeId = ad.VehicleSubtypeId, VehicleSubtypeName = ad.VehicleSubtype != null ? ad.VehicleSubtype.Name : null,
                Key = ad.Key, LabelPl = ad.LabelPl, DataType = ad.DataType, Unit = ad.Unit,
                ValidationJson = ad.ValidationJson, OptionsJson = ad.OptionsJson,
                IsRequired = ad.IsRequired, IsFilterable = ad.IsFilterable, IsSearchable = ad.IsSearchable,
                IsActive = ad.IsActive, SortOrder = ad.SortOrder,
                UsageCount = ad.Values.Count,
            }).ToListAsync();
        return Ok(new { items, total, page, pageSize });
    }

    [HttpPost]
    public async Task<IActionResult> CreateAttributeDefinition([FromBody] CreateAttributeDefinitionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Key) || string.IsNullOrWhiteSpace(dto.LabelPl))
            return BadRequest("Klucz i etykieta pola są wymagane.");
        if (!await _db.VehicleCategories.AnyAsync(c => c.Id == dto.VehicleCategoryId))
            return BadRequest("Nieznana kategoria.");
        if (dto.VehicleSubtypeId.HasValue && !await _db.VehicleSubtypes.AnyAsync(s => s.Id == dto.VehicleSubtypeId.Value && s.VehicleCategoryId == dto.VehicleCategoryId))
            return BadRequest("Podtyp nie należy do wskazanej kategorii.");
        if (await _db.AttributeDefinitions.AnyAsync(ad => ad.VehicleCategoryId == dto.VehicleCategoryId && ad.VehicleSubtypeId == dto.VehicleSubtypeId && ad.Key == dto.Key))
            return BadRequest($"Pole o kluczu '{dto.Key}' już istnieje w tym zakresie.");

        var def = new AttributeDefinition
        {
            VehicleCategoryId = dto.VehicleCategoryId, VehicleSubtypeId = dto.VehicleSubtypeId,
            Key = dto.Key, LabelPl = dto.LabelPl, DataType = dto.DataType, Unit = dto.Unit,
            ValidationJson = dto.ValidationJson, OptionsJson = dto.OptionsJson,
            IsRequired = dto.IsRequired, IsFilterable = dto.IsFilterable, IsSearchable = dto.IsSearchable,
            IsActive = dto.IsActive, SortOrder = dto.SortOrder,
        };
        _db.AttributeDefinitions.Add(def);
        await _db.SaveChangesAsync();
        _taxonomyCacheVersion.Bump();
        return Ok(new { def.Id });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAttributeDefinition(int id, [FromBody] CreateAttributeDefinitionDto dto)
    {
        var def = await _db.AttributeDefinitions.FirstOrDefaultAsync(ad => ad.Id == id);
        if (def == null) return NotFound();
        if (string.IsNullOrWhiteSpace(dto.Key) || string.IsNullOrWhiteSpace(dto.LabelPl))
            return BadRequest("Klucz i etykieta pola są wymagane.");

        def.VehicleCategoryId = dto.VehicleCategoryId;
        def.VehicleSubtypeId = dto.VehicleSubtypeId;
        def.Key = dto.Key;
        def.LabelPl = dto.LabelPl;
        def.DataType = dto.DataType;
        def.Unit = dto.Unit;
        def.ValidationJson = dto.ValidationJson;
        def.OptionsJson = dto.OptionsJson;
        def.IsRequired = dto.IsRequired;
        def.IsFilterable = dto.IsFilterable;
        def.IsSearchable = dto.IsSearchable;
        def.IsActive = dto.IsActive;
        def.SortOrder = dto.SortOrder;
        await _db.SaveChangesAsync();
        _taxonomyCacheVersion.Bump();
        return Ok(new { def.Id });
    }

    // Hard-delete only when nothing references it yet; otherwise the caller must soft-disable via
    // PUT (IsActive=false) - matches the DB-level Restrict FK on AdvertAttributeValue.
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAttributeDefinition(int id)
    {
        var def = await _db.AttributeDefinitions.Include(ad => ad.Values).FirstOrDefaultAsync(ad => ad.Id == id);
        if (def == null) return NotFound();
        if (def.Values.Count > 0)
            return BadRequest($"To pole ma już {def.Values.Count} zapisanych wartości - nie można go usunąć. Możesz je dezaktywować (przełącznik 'Aktywne').");
        _db.AttributeDefinitions.Remove(def);
        await _db.SaveChangesAsync();
        _taxonomyCacheVersion.Bump();
        return NoContent();
    }
}
