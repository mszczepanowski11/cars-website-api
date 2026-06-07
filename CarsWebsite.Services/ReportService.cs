using cars_website_api.CarsWebsite.DTOs.Report;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Services
{
    public class ReportService : IReportService
    {
        private readonly AppDbContext _context;

        public ReportService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<int> CreateReportAsync(CreateReportDto dto, int reportedByUserId)
        {
            if (dto.TargetType == ReportTargetType.Advert && dto.TargetAdvertId == null)
                throw new ArgumentException("TargetAdvertId is required for advert reports.");
            if (dto.TargetType == ReportTargetType.User && dto.TargetUserId == null)
                throw new ArgumentException("TargetUserId is required for user reports.");

            var report = new Report
            {
                TargetType = dto.TargetType,
                TargetAdvertId = dto.TargetAdvertId,
                TargetUserId = dto.TargetUserId,
                Reason = dto.Reason,
                Content = dto.Content,
                ReportedAt = DateTime.UtcNow,
                ReportedByUserId = reportedByUserId,
                Status = ReportStatus.Pending
            };

            _context.Reports.Add(report);
            await _context.SaveChangesAsync();
            return report.Id;
        }

        public async Task<PagedResult<ReportResponseDto>> GetUserReportsAsync(int userId, int page, int pageSize)
        {
            var query = _context.Reports
                .Include(r => r.ReportedBy)
                .Include(r => r.TargetAdvert)
                .Include(r => r.TargetUser)
                .Where(r => r.ReportedByUserId == userId)
                .OrderByDescending(r => r.ReportedAt);

            var totalCount = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return new PagedResult<ReportResponseDto>
            {
                TotalCount = totalCount,
                Items = items.Select(MapToDto).ToList()
            };
        }

        internal static ReportResponseDto MapToDto(Report r) => new()
        {
            Id = r.Id,
            TargetType = r.TargetType.ToString(),
            TargetAdvertId = r.TargetAdvertId,
            TargetAdvertTitle = r.TargetAdvert?.Title,
            TargetUserId = r.TargetUserId,
            TargetUserName = r.TargetUser != null ? $"{r.TargetUser.Name} {r.TargetUser.Surname}" : null,
            Reason = r.Reason.ToString(),
            Content = r.Content,
            ReportedAt = r.ReportedAt,
            ReportedByUserId = r.ReportedByUserId,
            ReportedByName = $"{r.ReportedBy.Name} {r.ReportedBy.Surname}",
            Status = r.Status.ToString(),
            ResolvedAt = r.ResolvedAt,
            AdminNote = r.AdminNote
        };
    }
}