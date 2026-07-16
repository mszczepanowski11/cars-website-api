using cars_website_api.CarsWebsite.DTOs.Partner;

namespace cars_website_api.CarsWebsite.Interfaces;

public interface IPartnerService
{
    Task<List<PartnerResponseDto>> GetAllAsync();
    Task<PartnerResponseDto> GetByIdAsync(int id);
    Task<(PartnerResponseDto Partner, string ApiKey)> CreateAsync(CreatePartnerDto dto);
    Task<PartnerResponseDto> UpdateAsync(int id, UpdatePartnerDto dto);
    Task<string> RegenerateApiKeyAsync(int id);
    Task DeleteAsync(int id);
    Task<List<PartnerImportLogResponseDto>> GetImportLogsAsync(int id, int limit = 20);

    // Authenticates the X-Api-Key header against ApiKeyHash; returns null if no active partner matches.
    Task<CarsWebsite.Partner?> AuthenticateAsync(string apiKey);
}
