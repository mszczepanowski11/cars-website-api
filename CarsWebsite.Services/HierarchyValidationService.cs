using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Services;

public class HierarchyValidationService : IHierarchyValidationService
{
    private readonly AppDbContext _context;

    public HierarchyValidationService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<ChainValidationResult> ValidateVehicleChainAsync(
        int brandId,
        int? modelId,
        int? generationId,
        int? trimId,
        int? engineVersionId,
        int? vehicleCategoryId)
    {
        if (vehicleCategoryId.HasValue && brandId > 0)
        {
            var brandInCategory = await _context.Brands
                .Where(b => b.Id == brandId)
                .AnyAsync(b => b.Categories.Any(c => c.Id == vehicleCategoryId.Value));
            if (!brandInCategory)
                return ChainValidationResult.Fail("category", "Wybrana marka nie należy do tej kategorii pojazdu.");
        }

        if (modelId.HasValue && brandId > 0)
        {
            var modelBelongsToBrand = await _context.Models
                .AnyAsync(m => m.Id == modelId.Value && m.BrandId == brandId);
            if (!modelBelongsToBrand)
                return ChainValidationResult.Fail("model", "Wybrany model nie należy do wybranej marki.");
        }

        if (generationId.HasValue && modelId.HasValue)
        {
            var generationBelongsToModel = await _context.Generations
                .AnyAsync(g => g.Id == generationId.Value && g.ModelId == modelId.Value);
            if (!generationBelongsToModel)
                return ChainValidationResult.Fail("generation", "Wybrana generacja nie należy do wybranego modelu.");
        }

        if (trimId.HasValue && generationId.HasValue)
        {
            var trimBelongsToGeneration = await _context.Trims
                .AnyAsync(t => t.Id == trimId.Value && t.GenerationId == generationId.Value);
            if (!trimBelongsToGeneration)
                return ChainValidationResult.Fail("trim", "Wybrana wersja wyposażenia nie należy do wybranej generacji.");
        }

        if (engineVersionId.HasValue && generationId.HasValue)
        {
            var engineBelongsToGeneration = await _context.EngineVersions
                .AnyAsync(e => e.Id == engineVersionId.Value
                    && e.GenerationId == generationId.Value
                    && (e.TrimId == null || !trimId.HasValue || e.TrimId == trimId.Value));
            if (!engineBelongsToGeneration)
                return ChainValidationResult.Fail("engine", "Wybrana wersja silnika nie należy do wybranej generacji lub wersji wyposażenia.");
        }

        return ChainValidationResult.Ok();
    }

    public async Task<ChainValidationResult> ValidateEnginePlausibilityAsync(
        int brandId,
        int? fuelTypeId,
        int? engineVersionId,
        int? trimId,
        int? powerHP)
    {
        if (fuelTypeId.HasValue)
        {
            var brandHasRestriction = await _context.BrandAllowedFuelTypes.AnyAsync(x => x.BrandId == brandId);
            if (brandHasRestriction)
            {
                var allowed = await _context.BrandAllowedFuelTypes
                    .AnyAsync(x => x.BrandId == brandId && x.FuelTypeId == fuelTypeId.Value);
                if (!allowed)
                    return ChainValidationResult.Fail("fuelType", "Wybrany rodzaj paliwa nie jest dostępny dla tej marki.");
            }
        }

        string? engineName = null;
        string? trimName = null;
        var effectivePower = powerHP;

        if (engineVersionId.HasValue)
        {
            var engine = await _context.EngineVersions.AsNoTracking()
                .Where(e => e.Id == engineVersionId.Value)
                .Select(e => new { e.EngineName, e.PowerHP })
                .FirstOrDefaultAsync();
            if (engine != null)
            {
                engineName = engine.EngineName;
                effectivePower ??= engine.PowerHP;
            }
        }

        if (trimId.HasValue)
        {
            trimName = await _context.Trims.AsNoTracking()
                .Where(t => t.Id == trimId.Value)
                .Select(t => t.Name)
                .FirstOrDefaultAsync();
        }

        if (effectivePower.HasValue && (engineName != null || trimName != null))
        {
            var rules = await _context.ModelNamePlausibilityRules.AsNoTracking().ToListAsync();
            foreach (var rule in rules)
            {
                var matches =
                    (engineName != null && engineName.Contains(rule.NamePattern, StringComparison.OrdinalIgnoreCase)) ||
                    (trimName != null && trimName.Contains(rule.NamePattern, StringComparison.OrdinalIgnoreCase));
                if (matches && effectivePower.Value < rule.MinPowerHP)
                    return ChainValidationResult.Fail("power",
                        $"Wersja \"{rule.NamePattern}\" nie powinna mieć mniej niż {rule.MinPowerHP} KM (podano {effectivePower.Value} KM).");
            }
        }

        return ChainValidationResult.Ok();
    }

    public async Task<TaxonomyAuditReport> GetAuditReportAsync()
    {
        var report = new TaxonomyAuditReport();

        // FeatureCategory.VehicleCategoryId is now a required (NOT NULL) column at the DB level
        // (see the migration around this change), so the null-scope leak this used to check for
        // is structurally impossible — nothing to report here anymore.

        report.MismatchedEngineTrimGeneration = await _context.EngineVersions
            .Where(e => e.TrimId != null)
            .Include(e => e.Trim)
            .Where(e => e.Trim!.GenerationId != e.GenerationId)
            .Select(e => $"EngineVersion #{e.Id} \"{e.EngineName}\" (GenerationId={e.GenerationId}) vs Trim #{e.TrimId} (Trim.GenerationId={e.Trim!.GenerationId})")
            .ToListAsync();

        report.DuplicateBrandNames = await _context.Brands
            .GroupBy(b => b.Name)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} (x{g.Count()})")
            .ToListAsync();

        return report;
    }
}
