using cars_website_api.CarsWebsite.DTOs.Category;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Services;

public class CategoryService: ICategoryService
{
    private readonly AppDbContext _context;
    public CategoryService(AppDbContext context) => _context = context;
    
    public async Task<List<CategoryWithCountDto>> GetCategoriesWithCountsAsync()
    {
        var categoriesTask = _context.VehicleCategories
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ToListAsync();

        var countsTask = _context.CarAdverts
            .AsNoTracking()
            .Where(a => a.IsActive && !a.IsHidden)
            .GroupBy(a => a.VehicleCategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToListAsync();

        await Task.WhenAll(categoriesTask, countsTask);
        var categories = categoriesTask.Result;
        var counts = countsTask.Result;

        return categories.Select(c => new CategoryWithCountDto
        {
            Id = c.Id,
            Slug = c.Slug,
            Name = c.Name,
            Description = c.Description,
            IconName = c.IconName,
            AdvertCount = counts.FirstOrDefault(x => x.CategoryId == c.Id)?.Count ?? 0
        }).ToList();
    }
    
}