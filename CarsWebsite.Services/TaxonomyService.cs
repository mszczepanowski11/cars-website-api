using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using DriveType = cars_website_api.CarsWebsite.Domain.Entities.DriveType;

namespace cars_website_api.CarsWebsite.Services;

public class TaxonomyService : ITaxonomyService
{
    private readonly AppDbContext _context;
    private readonly IMemoryCache _cache;

    // B-27: Taxonomy data changes rarely — cache for 1 hour to avoid repeated DB queries.
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    public TaxonomyService(AppDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<IEnumerable<Brand>> GetFullTaxonomyAsync()
    {
        return await _cache.GetOrCreateAsync("taxonomy:full", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.Brands
                .AsNoTracking()
                .Include(b => b.Models)
                .ThenInclude(m => m.Generations)
                .ThenInclude(g => g.EngineVersions)
                .OrderBy(b => b.Name)
                .ToListAsync();
        }) ?? [];
    }

    public async Task<IEnumerable<Brand>> GetBrandsAsync()
    {
        return await _cache.GetOrCreateAsync("taxonomy:brands", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.Brands
                .AsNoTracking()
                .OrderBy(b => b.Name)
                .ToListAsync();
        }) ?? [];
    }

    public async Task<IEnumerable<Brand>> GetBrandsByCategoryAsync(int categoryId)
    {
        return await _cache.GetOrCreateAsync($"taxonomy:brands:category:{categoryId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.Brands
                .FromSqlRaw(@"
                    SELECT b.* FROM brands b
                    INNER JOIN brandvehiclecategories bvc ON bvc.BrandsId = b.Id
                    WHERE bvc.CategoriesId = {0}
                    ORDER BY b.Name", categoryId)
                .AsNoTracking()
                .ToListAsync();
        }) ?? [];
    }

    public async Task<IEnumerable<Model>> GetModelsByBrandAsync(int brandId)
    {
        return await _cache.GetOrCreateAsync($"taxonomy:models:brand:{brandId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.Models
                .AsNoTracking()
                .Where(m => m.BrandId == brandId)
                .OrderBy(m => m.Name)
                .ToListAsync();
        }) ?? [];
    }

    public async Task<IEnumerable<Generation>> GetGenerationsByModelAsync(int modelId)
    {
        return await _cache.GetOrCreateAsync($"taxonomy:generations:model:{modelId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.Generations
                .AsNoTracking()
                .Where(g => g.ModelId == modelId)
                .OrderBy(g => g.YearFrom)
                .ToListAsync();
        }) ?? [];
    }

    public async Task<IEnumerable<EngineVersion>> GetEnginesByGenerationAsync(int generationId)
    {
        return await _cache.GetOrCreateAsync($"taxonomy:engines:generation:{generationId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.EngineVersions
                .AsNoTracking()
                .Include(e => e.FuelType)
                .Where(e => e.GenerationId == generationId)
                .OrderBy(e => e.EngineName)
                .ToListAsync();
        }) ?? [];
    }

    public async Task<IEnumerable<FuelType>> GetFuelTypesAsync()
    {
        return await _cache.GetOrCreateAsync("taxonomy:fueltypes", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.FuelTypes
                .AsNoTracking()
                .OrderBy(f => f.Name)
                .ToListAsync();
        }) ?? [];
    }

    public async Task<IEnumerable<Gearbox>> GetGearboxesAsync()
    {
        return await _cache.GetOrCreateAsync("taxonomy:gearboxes", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.Gearboxes
                .AsNoTracking()
                .OrderBy(g => g.Name)
                .ToListAsync();
        }) ?? [];
    }

    public async Task<IEnumerable<BodyType>> GetBodyTypesAsync()
    {
        return await _cache.GetOrCreateAsync("taxonomy:bodytypes", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.BodyTypes
                .AsNoTracking()
                .OrderBy(b => b.Name)
                .ToListAsync();
        }) ?? [];
    }

    public async Task<IEnumerable<DriveType>> GetDriveTypesAsync()
    {
        return await _cache.GetOrCreateAsync("taxonomy:drivetypes", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.DriveTypes
                .AsNoTracking()
                .OrderBy(d => d.Id)
                .ToListAsync();
        }) ?? [];
    }

    public async Task<IEnumerable<CarColor>> GetColorsAsync()
    {
        return await _cache.GetOrCreateAsync("taxonomy:colors", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.CarColors
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .ToListAsync();
        }) ?? [];
    }

    public async Task<IEnumerable<Feature>> GetFeaturesAsync()
    {
        return await _cache.GetOrCreateAsync("taxonomy:features", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.Features
                .AsNoTracking()
                .Include(f => f.Category)
                .OrderBy(f => f.Name)
                .ToListAsync();
        }) ?? [];
    }

    public async Task<IEnumerable<VehicleCategory>> GetVehicleCategoriesAsync()
    {
        return await _cache.GetOrCreateAsync("taxonomy:vehiclecategories", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.VehicleCategories
                .AsNoTracking()
                .OrderBy(c => c.SortOrder)
                .ToListAsync();
        }) ?? [];
    }

    public async Task<IEnumerable<FeatureCategory>> GetFeatureCategoriesAsync()
    {
        return await _cache.GetOrCreateAsync("taxonomy:featurecategories", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.FeatureCategories
                .AsNoTracking()
                .Include(fc => fc.Features)
                .OrderBy(fc => fc.Name)
                .ToListAsync();
        }) ?? [];
    }

    public async Task<IEnumerable<FeatureCategory>> GetFeatureCategoriesByVehicleCategoryAsync(int vehicleCategoryId)
    {
        return await _cache.GetOrCreateAsync($"taxonomy:featurecategories:vehicle:{vehicleCategoryId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.FeatureCategories
                .AsNoTracking()
                .Include(fc => fc.Features)
                .Where(fc => fc.VehicleCategoryId == vehicleCategoryId || fc.VehicleCategoryId == null)
                .OrderBy(fc => fc.Name)
                .ToListAsync();
        }) ?? [];
    }

    public async Task<IEnumerable<FeatureCategory>> GetFeatureCategoriesByContextAsync(int? vehicleCategoryId, int? brandId, int? modelId)
    {
        var cacheKey = $"taxonomy:featurecategories:context:{vehicleCategoryId}:{brandId}:{modelId}";
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.FeatureCategories
                .AsNoTracking()
                .Include(fc => fc.Features)
                .Where(fc =>
                    (fc.VehicleCategoryId == null || fc.VehicleCategoryId == vehicleCategoryId) &&
                    (fc.BrandId == null || fc.BrandId == brandId) &&
                    (fc.ModelId == null || fc.ModelId == modelId))
                .OrderBy(fc => fc.Name)
                .ToListAsync();
        }) ?? [];
    }

    public async Task<IEnumerable<Trim>> GetTrimsByGenerationAsync(int generationId)
    {
        return await _cache.GetOrCreateAsync($"taxonomy:trims:generation:{generationId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.Trims
                .AsNoTracking()
                .Where(t => t.GenerationId == generationId)
                .OrderBy(t => t.Name)
                .ToListAsync();
        }) ?? [];
    }

    public async Task<IEnumerable<EngineVersion>> GetEnginesByTrimAsync(int trimId)
    {
        return await _cache.GetOrCreateAsync($"taxonomy:engines:trim:{trimId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.EngineVersions
                .AsNoTracking()
                .Include(e => e.FuelType)
                .Where(e => e.TrimId == trimId)
                .OrderBy(e => e.EngineName)
                .ToListAsync();
        }) ?? [];
    }

    public async Task<EngineVersion?> GetEngineSpecsAsync(int engineVersionId)
    {
        return await _cache.GetOrCreateAsync($"taxonomy:enginespecs:{engineVersionId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.EngineVersions
                .AsNoTracking()
                .Include(e => e.FuelType)
                .FirstOrDefaultAsync(e => e.Id == engineVersionId);
        });
    }

    public async Task<IEnumerable<VehicleSubtype>> GetVehicleSubtypesByCategoryAsync(int vehicleCategoryId)
    {
        return await _cache.GetOrCreateAsync($"taxonomy:vehiclesubtypes:category:{vehicleCategoryId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.VehicleSubtypes
                .AsNoTracking()
                .Where(vs => vs.VehicleCategoryId == vehicleCategoryId)
                .OrderBy(vs => vs.SortOrder)
                .ThenBy(vs => vs.Name)
                .ToListAsync();
        }) ?? [];
    }

    public async Task<IEnumerable<PartCategory>> GetPartCategoriesAsync()
    {
        return await _cache.GetOrCreateAsync("taxonomy:partcategories", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.PartCategories
                .AsNoTracking()
                .Include(pc => pc.Subcategories)
                .OrderBy(pc => pc.SortOrder)
                .ThenBy(pc => pc.Name)
                .ToListAsync();
        }) ?? [];
    }

    public async Task<IEnumerable<PartSubcategory>> GetPartSubcategoriesByCategoryAsync(int partCategoryId)
    {
        return await _cache.GetOrCreateAsync($"taxonomy:partsubcategories:category:{partCategoryId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _context.PartSubcategories
                .AsNoTracking()
                .Where(ps => ps.PartCategoryId == partCategoryId)
                .OrderBy(ps => ps.SortOrder)
                .ThenBy(ps => ps.Name)
                .ToListAsync();
        }) ?? [];
    }
}
