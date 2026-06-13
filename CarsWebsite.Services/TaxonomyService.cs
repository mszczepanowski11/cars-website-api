using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;
using DriveType = cars_website_api.CarsWebsite.Domain.Entities.DriveType;

namespace cars_website_api.CarsWebsite.Services;

public class TaxonomyService : ITaxonomyService
{
    private readonly AppDbContext _context;

    public TaxonomyService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Brand>> GetFullTaxonomyAsync()
    {
        return await _context.Brands
            .Include(b => b.Models)
            .ThenInclude(m => m.Generations)
            .ThenInclude(g => g.EngineVersions)
            .OrderBy(b => b.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Brand>> GetBrandsAsync()
    {
        return await _context.Brands
            .OrderBy(b => b.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Brand>> GetBrandsByCategoryAsync(int categoryId)
    {
        return await _context.Brands
            .FromSqlRaw(@"
                SELECT b.* FROM Brands b
                INNER JOIN BrandVehicleCategories bvc ON bvc.BrandsId = b.Id
                WHERE bvc.CategoriesId = {0}
                ORDER BY b.Name", categoryId)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IEnumerable<Model>> GetModelsByBrandAsync(int brandId)
    {
        return await _context.Models
            .Where(m => m.BrandId == brandId)
            .OrderBy(m => m.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Generation>> GetGenerationsByModelAsync(int modelId)
    {
        return await _context.Generations
            .Where(g => g.ModelId == modelId)
            .OrderBy(g => g.YearFrom)
            .ToListAsync();
    }

    public async Task<IEnumerable<EngineVersion>> GetEnginesByGenerationAsync(int generationId)
    {
        return await _context.EngineVersions
            .Where(e => e.GenerationId == generationId)
            .OrderBy(e => e.EngineName)
            .ToListAsync();
    }

    public async Task<IEnumerable<FuelType>> GetFuelTypesAsync()
    {
        return await _context.FuelTypes
            .OrderBy(f => f.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Gearbox>> GetGearboxesAsync()
    {
        return await _context.Gearboxes
            .OrderBy(g => g.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<BodyType>> GetBodyTypesAsync()
    {
        return await _context.BodyTypes
            .OrderBy(b => b.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<DriveType>> GetDriveTypesAsync()
    {
        return await _context.DriveTypes
            .OrderBy(d => d.Id)
            .ToListAsync();
    }

    public async Task<IEnumerable<CarColor>> GetColorsAsync()
    {
        return await _context.CarColors
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Feature>> GetFeaturesAsync()
    {
        return await _context.Features
            .OrderBy(f => f.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<VehicleCategory>> GetVehicleCategoriesAsync()
    {
        return await _context.VehicleCategories
            .OrderBy(c => c.SortOrder)
            .ToListAsync();
    }

    public async Task<IEnumerable<FeatureCategory>> GetFeatureCategoriesAsync()
    {
        return await _context.FeatureCategories
            .Include(fc => fc.Features)
            .OrderBy(fc => fc.Name)
            .ToListAsync();
    }
}
