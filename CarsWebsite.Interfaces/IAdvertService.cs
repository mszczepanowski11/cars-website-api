using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.DTOs.Advert;

public interface IAdvertService
{
    Task<int> CreateCarAdvertAsync(CreateCarAdvertDto dto, int userId);
    Task UpdateCarAdvertAsync(int id, UpdateCarAdvertDto dto, int userId, bool isAdmin = false);
    Task DeleteCarAdvertAsync(int id, int userId);
    Task<CarAdvertResponseDto> GetCarAdvertByIdAsync(int id, int? requestingUserId = null, bool isAdmin = false);
    Task<PagedResult<CarAdvertResponseDto>> SearchCarAdvertsAsync(SearchCarAdvertDto dto);
    Task<PagedResult<CarAdvertResponseDto>> GetUserAdvertsAsync(int userId, int page = 1, int pageSize = 20);
    Task PromoteAdvertAsync(int advertId, int userId, string type, int durationDays, bool isAdmin = false);
    Task<CarAdvertResponseDto?> GetByVinAsync(string vin);
    Task MarkAsSoldAsync(int advertId, int userId);
    Task PublishAsync(int advertId, int userId);
    Task<(int activeCount, int yearCount)> GetPersonalAdCountsAsync(int userId);
    Task DeactivateAsync(int advertId, int userId);
    Task RenewAsync(int advertId, int userId);
    Task<List<CarAdvertResponseDto>> GetMostViewedAsync(int count = 8);
    Task<List<CarAdvertResponseDto>> GetPremiumCollectionAsync(int count = 8);
    Task RecordViewAsync(int advertId, string? ipAddress, int? viewerUserId = null);
    Task<CarAdvert?> GetCarAdvertEntityAsync(int advertId);
    Task SetPdfBrochureUrlAsync(int advertId, string? url);

    // Bulk self-serve tooling (CTO audit Etap 3) - dealers with dozens/hundreds of adverts had no
    // way to act on more than one at a time before this.
    Task<BulkActionResultDto> BulkActionAsync(List<int> ids, string action, int userId);
    Task<string> ExportUserAdvertsCsvAsync(int userId);
}

public class PagedResult<T>
{
    public List<T> Items { get; set; }
    public int TotalCount { get; set; }
}
