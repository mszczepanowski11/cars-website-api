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
                .Include(fc => fc.Features)
                .Where(fc =>
                    (fc.VehicleCategoryId == null || fc.VehicleCategoryId == vehicleCategoryId) &&
                    (fc.BrandId == null || fc.BrandId == brandId) &&
                    (fc.ModelId == null || fc.ModelId == modelId))
                .OrderBy(fc => fc.Name)
                .ToListAsync();
        }) ?? [];
    }
}
