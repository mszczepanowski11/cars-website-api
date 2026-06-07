using cars_website_api.CarsWebsite.DTOs.Report;
using CarsWebsite;

namespace cars_website_api.CarsWebsite.Interfaces
{
    public interface IReportService
    {
        Task<int> CreateReportAsync(CreateReportDto dto, int reportedByUserId);
        Task<PagedResult<ReportResponseDto>> GetUserReportsAsync(int userId, int page, int pageSize);
    }
}