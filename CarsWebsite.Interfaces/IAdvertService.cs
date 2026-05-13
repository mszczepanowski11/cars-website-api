using cars_website_api.CarsWebsite.DTOs.Advert;

public interface IAdvertService
{
    Task<int> CreateCarAdvertAsync(CreateCarAdvertDto dto,int userId);
    Task UpdateCarAdvertAsync(int id, UpdateCarAdvertDto dto, int userId);
    Task DeleteCarAdvertAsync(int id, int userId);
    Task<CarAdvertResponseDto> GetCarAdvertByIdAsync(int id);
    Task<PagedResult<CarAdvertResponseDto>> SearchCarAdvertsAsync(SearchCarAdvertDto dto);
    Task<PagedResult<CarAdvertResponseDto>> GetUserAdvertsAsync(int userId, int page = 1, int pageSize = 20);
}

public class PagedResult<T>
{
    public List<T> Items { get; set; }
    public int TotalCount { get; set; }
}