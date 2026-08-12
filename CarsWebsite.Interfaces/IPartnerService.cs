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

    // Fetches the partner's FeedUrl right now and imports it, outside the 6h PartnerFeedSyncJob
    // schedule - throws InvalidOperationException (caught by the controller as 400) if the partner
    // has no FeedUrl configured or the fetch fails, so the admin gets an immediate, specific reason
    // instead of having to wait for the next cron cycle to find out something's wrong.
    Task<PartnerImportLogResponseDto> SyncNowAsync(int id);

    // Schema adapter layer (CTO audit Etap 2) - per-partner field/value mapping so onboarding a
    // new integrator (AKOL, 44FOX, ...) with its own feed schema is a config change, not new code.
    Task<List<PartnerFieldMappingDto>> GetFieldMappingsAsync(int partnerId);
    Task<List<PartnerFieldMappingDto>> SetFieldMappingsAsync(int partnerId, List<PartnerFieldMappingDto> mappings);
    Task<List<PartnerValueMappingDto>> GetValueMappingsAsync(int partnerId);
    Task<List<PartnerValueMappingDto>> SetValueMappingsAsync(int partnerId, List<PartnerValueMappingDto> mappings);

    // Authenticates the X-Api-Key header against ApiKeyHash; returns null if no active partner matches.
    // global:: because plain "CarsWebsite.Partner" resolves against the enclosing
    // cars_website_api.CarsWebsite namespace here and fails to compile.
    Task<global::CarsWebsite.Partner?> AuthenticateAsync(string apiKey);
}
