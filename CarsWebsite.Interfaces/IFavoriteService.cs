using cars_website_api.CarsWebsite.DTOs.Advert;

namespace cars_website_api.CarsWebsite.Interfaces;

public interface IFavoriteService
{
    Task AddFavoriteAsync(int userId, int advertId);
    Task RemoveFavoriteAsync(int userId, int advertId);
    Task<PagedResult<CarAdvertResponseDto>> GetUserFavoritesAsync(int userId, int page, int pageSize);
    Task<bool> IsFavoriteAsync(int userId, int advertId);
}