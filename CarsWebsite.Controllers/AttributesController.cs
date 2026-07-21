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

    // Public: GET /api/Attributes/values/{advertId} - the saved values for one advert, so the
    // edit form can pre-fill DynamicAttributeField inputs the same way it already pre-fills the
    // hardcoded CarAdvert columns. Not auth-gated (matches GetAdvert/{id} being public too).
    [HttpGet("values/{advertId}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetAdvertAttributeValues(int advertId)
    {
        var values = await _db.AdvertAttributeValues.AsNoTracking()
            .Where(v => v.AdvertId == advertId)
            .Select(v => new AdvertAttributeValueDto
            {
                AttributeDefinitionId = v.AttributeDefinitionId,
                ValueText = v.ValueText, ValueNumber = v.ValueNumber, ValueBool = v.ValueBool, ValueDate = v.ValueDate,
            }).ToListAsync();
        return Ok(values);
    }

    // Public: GET /api/Attributes?categoryId=5&subtypeId=12&activeOnly=true
    // Returns definitions scoped to the whole category (VehicleSubtypeId == null) plus, if a
    // subtypeId is given, the ones scoped to that specific subtype - mirrors how
    // CATEGORY_CONFIGS.extraFields + SUBTYPE_EXTRA_FIELDS combine today.
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAttributeDefinitions(
        [FromQuery] int categoryId, [FromQuery] int? subtypeId,
        [FromQuery] int? brandId, [FromQuery] int? modelId, [FromQuery] int? generationId, [FromQuery] int? trimId,
        [FromQuery] bool activeOnly = true)
    {
        // Wildcard scoping ("inteligentny formularz"): a definition applies when, at every level, its
        // scope is either null (= "any") or equals the selected vehicle. So a BMW-only field
        // (BrandId=BMW, rest null) appears for any BMW; an F10-only field (GenerationId=F10) appears
        // only for that generation; category-wide fields (all null) always appear.
        var q = _db.AttributeDefinitions.AsNoTracking()
            .Where(ad => ad.VehicleCategoryId == categoryId
                && (ad.VehicleSubtypeId == null || ad.VehicleSubtypeId == subtypeId)
                && (ad.BrandId == null || ad.BrandId == brandId)
                && (ad.ModelId == null || ad.ModelId == modelId)
                && (ad.GenerationId == null || ad.GenerationId == generationId)
                && (ad.TrimId == null || ad.TrimId == trimId));
        if (activeOnly) q = q.Where(ad => ad.IsActive);

        var items = await q.OrderBy(ad => ad.SortOrder).ThenBy(ad => ad.Id)
            .Select(ad => new AttributeDefinitionDto
            {
                Id = ad.Id, VehicleCategoryId = ad.VehicleCategoryId, VehicleSubtypeId = ad.VehicleSubtypeId,
                BrandId = ad.BrandId, ModelId = ad.ModelId, GenerationId = ad.GenerationId, TrimId = ad.TrimId,
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
                BrandId = ad.BrandId, ModelId = ad.ModelId, GenerationId = ad.GenerationId, TrimId = ad.TrimId,
                Key = ad.Key, LabelPl = ad.LabelPl, DataType = ad.DataType, Unit = ad.Unit,
                ValidationJson = ad.ValidationJson, OptionsJson = ad.OptionsJson,
                IsRequired = ad.IsRequired, IsFilterable = ad.IsFilterable, IsSearchable = ad.IsSearchable,
                IsActive = ad.IsActive, SortOrder = ad.SortOrder,
                UsageCount = ad.Values.Count,
            }).ToListAsync();

        // Resolve vehicle-scope names in-memory (Model.Name is a computed property EF can't project).
        // Only the IDs actually present on this page are looked up, so the cost is bounded.
        await ResolveScopeNamesAsync(items);
        return Ok(new { items, total, page, pageSize });
    }

    private async Task ResolveScopeNamesAsync(List<AttributeDefinitionDto> items)
    {
        var brandIds = items.Where(i => i.BrandId.HasValue).Select(i => i.BrandId!.Value).Distinct().ToList();
        var modelIds = items.Where(i => i.ModelId.HasValue).Select(i => i.ModelId!.Value).Distinct().ToList();
        var genIds = items.Where(i => i.GenerationId.HasValue).Select(i => i.GenerationId!.Value).Distinct().ToList();
        var trimIds = items.Where(i => i.TrimId.HasValue).Select(i => i.TrimId!.Value).Distinct().ToList();

        var brands = brandIds.Count == 0 ? new() : await _db.Brands.AsNoTracking()
            .Where(b => brandIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name);
        var models = modelIds.Count == 0 ? new() : (await _db.Models.AsNoTracking()
            .Where(m => modelIds.Contains(m.Id)).ToListAsync()).ToDictionary(m => m.Id, m => m.Name);
        var gens = genIds.Count == 0 ? new() : await _db.Generations.AsNoTracking()
            .Where(g => genIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name);
        var trims = trimIds.Count == 0 ? new() : await _db.Trims.AsNoTracking()
            .Where(t => trimIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name);

        foreach (var i in items)
        {
            if (i.BrandId.HasValue && brands.TryGetValue(i.BrandId.Value, out var bn)) i.BrandName = bn;
            if (i.ModelId.HasValue && models.TryGetValue(i.ModelId.Value, out var mn)) i.ModelName = mn;
            if (i.GenerationId.HasValue && gens.TryGetValue(i.GenerationId.Value, out var gn)) i.GenerationName = gn;
            if (i.TrimId.HasValue && trims.TryGetValue(i.TrimId.Value, out var tn)) i.TrimName = tn;
        }
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
        // Uniqueness is per exact scope, so the same key (e.g. "xDrive") can exist for different
        // brands/models without colliding - only a duplicate within the identical scope is rejected.
        if (await _db.AttributeDefinitions.AnyAsync(ad => ad.VehicleCategoryId == dto.VehicleCategoryId
                && ad.VehicleSubtypeId == dto.VehicleSubtypeId && ad.BrandId == dto.BrandId
                && ad.ModelId == dto.ModelId && ad.GenerationId == dto.GenerationId && ad.TrimId == dto.TrimId
                && ad.Key == dto.Key))
            return BadRequest($"Pole o kluczu '{dto.Key}' już istnieje w tym zakresie.");

        var def = new AttributeDefinition
        {
            VehicleCategoryId = dto.VehicleCategoryId, VehicleSubtypeId = dto.VehicleSubtypeId,
            BrandId = dto.BrandId, ModelId = dto.ModelId, GenerationId = dto.GenerationId, TrimId = dto.TrimId,
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
        def.BrandId = dto.BrandId;
        def.ModelId = dto.ModelId;
        def.GenerationId = dto.GenerationId;
        def.TrimId = dto.TrimId;
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
