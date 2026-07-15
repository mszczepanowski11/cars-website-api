using cars_website_api.CarsWebsite.DTOs.SavedSearch;

namespace cars_website_api.CarsWebsite.Interfaces;

public interface ISavedSearchService
{
    Task<PagedResult<SavedSearchResponseDto>> GetMyAsync(int userId, int page, int pageSize);
    Task<SavedSearchResponseDto> CreateAsync(int userId, CreateSavedSearchDto dto);
    Task<SavedSearchResponseDto> UpdateAsync(int id, int userId, UpdateSavedSearchDto dto);
    Task DeleteAsync(int id, int userId);
    Task SetNotifyAsync(int id, int userId, bool notifyOnNew);
}
