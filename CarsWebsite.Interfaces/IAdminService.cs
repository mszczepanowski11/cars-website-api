using cars_website_api.CarsWebsite.DTOs.Admin;
using cars_website_api.CarsWebsite.DTOs.Report;
using CarsWebsite;

namespace cars_website_api.CarsWebsite.Interfaces
{
    public interface IAdminService
    {
        Task<AdminStatsDto> GetStatsAsync();
        Task<PagedResult<ReportResponseDto>> GetReportsAsync(AdminReportFilterDto filter);
        Task<ReportResponseDto?> GetReportByIdAsync(int id);
        Task ResolveReportAsync(int reportId, int adminUserId, string? note);
        Task RejectReportAsync(int reportId, int adminUserId, string? note);
        Task HideAdvertAsync(int advertId, int adminUserId, string? note);
        Task UnhideAdvertAsync(int advertId, int adminUserId);
        Task DeleteAdvertAsync(int advertId, int adminUserId, string? note);
        Task ActivateAdvertAsync(int advertId, int adminUserId);
        Task DeactivateAdvertAsync(int advertId, int adminUserId);
        Task BlockUserAsync(int userId, int adminUserId, string? reason);
        Task UnblockUserAsync(int userId, int adminUserId);
        Task DeleteUserAsync(int userId, int adminUserId, string? note);
        Task<PagedResult<AdminUserDto>> GetUsersAsync(string? search, string? accountType, bool? isBlocked, int page, int pageSize);
        Task<PagedResult<AdminAdvertDto>> GetAdvertsAsync(string? search, bool? isHidden, bool? isActive, int page, int pageSize);
        Task<List<AdminActionLogDto>> GetActionLogsAsync(int page, int pageSize);
        Task<AdminCreateClientAdvertResultDto> CreateClientAdvertAsync(AdminCreateClientAdvertDto dto, int adminUserId);
        Task ResendClientActivationEmailAsync(int userId, int adminUserId);
        Task ActivateUserAsync(int userId, int adminUserId);
    }
}