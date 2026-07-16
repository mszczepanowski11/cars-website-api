using cars_website_api.CarsWebsite.DTOs.Partner;

namespace cars_website_api.CarsWebsite.Interfaces;

public interface IPartnerSignupService
{
    // Fetches/parses FeedUrl (if given) and returns a count - does not persist anything.
    Task<PartnerSignupPreviewResultDto> PreviewAsync(PartnerSignupInputDto dto);

    // Persists a Pending PartnerSignupRequest for admin review.
    Task<PartnerSignupResponseDto> SubmitAsync(PartnerSignupInputDto dto);

    Task<List<PartnerSignupRequestListDto>> GetAllAsync(string? status);
    Task<ApprovePartnerSignupResultDto> ApproveAsync(int id, int adminUserId);
    Task RejectAsync(int id, int adminUserId, string? reason);
}
