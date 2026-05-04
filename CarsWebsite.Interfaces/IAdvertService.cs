using cars_website_api.CarsWebsite.DTOs.Advert;

public interface IAdvertService
{
    Task<int> CreateCarAdvertAsync(CreateCarAdvertDto dto);
    Task UpdateCarAdvertAsync(int id, UpdateCarAdvertDto dto);
    Task DeleteCarAdvertAsync(int id);
    Task<CarAdvertResponseDto> GetCarAdvertByIdAsync(int id);
    Task<PagedResult<CarAdvertResponseDto>> SearchCarAdvertsAsync(SearchCarAdvertDto dto);
}

public class PagedResult<T>
{
    public List<T> Items { get; set; }
    public int TotalCount { get; set; }
}