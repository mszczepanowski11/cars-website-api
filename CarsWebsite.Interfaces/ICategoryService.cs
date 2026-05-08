using cars_website_api.CarsWebsite.DTOs.Category;

namespace cars_website_api.CarsWebsite.Interfaces;

public interface ICategoryService
{
    Task<List<CategoryWithCountDto>> GetCategoriesWithCountsAsync();

}