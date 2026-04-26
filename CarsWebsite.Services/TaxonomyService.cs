using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;

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
    
    public async Task<IEnumerable<Feature>> GetFeaturesAsync()
    {
        return await _context.Features
            .OrderBy(f => f.Name)
            .ToListAsync();
    }
}
