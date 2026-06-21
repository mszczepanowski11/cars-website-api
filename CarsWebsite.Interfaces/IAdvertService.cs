using cars_website_api.CarsWebsite.DTOs.Advert;

public interface IAdvertService
{
    Task<int> CreateCarAdvertAsync(CreateCarAdvertDto dto, int userId);
    Task UpdateCarAdvertAsync(int id, UpdateCarAdvertDto dto, int userId);
    Task DeleteCarAdvertAsync(int id, int userId);
    Task<CarAdvertResponseDto> GetCarAdvertByIdAsync(int id, int? requestingUserId = null, bool isAdmin = false);
    Task<PagedResult<CarAdvertResponseDto>> SearchCarAdvertsAsync(SearchCarAdvertDto dto);
    Task<PagedResult<CarAdvertResponseDto>> GetUserAdvertsAsync(int userId, int page = 1, int pageSize = 20);
    Task PromoteAdvertAsync(int advertId, int userId, string type, int durationDays);
    Task<CarAdvertResponseDto?> GetByVinAsync(string vin);
    Task MarkAsSoldAsync(int advertId, int userId);
    Task PublishAsync(int advertId, int userId);
    Task<(int activeCount, int yearCount)> GetPersonalAdCountsAsync(int userId);
    Task DeactivateAsync(int advertId, int userId);
    Task RenewAsync(int advertId, int userId);
    Task<List<CarAdvertResponseDto>> GetMostViewedAsync(int count = 8);
    Task<List<CarAdvertResponseDto>> GetPremiumCollectionAsync(int count = 8);
    Task RecordViewAsync(int advertId, string? ipAddress);
}

public class PagedResult<T>
{
    public List<T> Items { get; set; }
    public int TotalCount { get; set; }
}
