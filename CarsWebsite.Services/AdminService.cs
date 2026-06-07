using cars_website_api.CarsWebsite.DTOs.Admin;
using cars_website_api.CarsWebsite.DTOs.Report;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Services
{
    public class AdminService : IAdminService
    {
        private readonly AppDbContext _context;

        public AdminService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<AdminStatsDto> GetStatsAsync()
        {
            var now = DateTime.UtcNow;
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            return new AdminStatsDto
            {
                TotalActiveAdverts = await _context.CarAdverts.CountAsync(a => a.IsActive && !a.IsHidden),
                TotalUsers = await _context.Users.CountAsync(),
                TotalReports = await _context.Reports.CountAsync(),
                PendingReports = await _context.Reports.CountAsync(r => r.Status == ReportStatus.Pending),
                NewRegistrationsThisMonth = 0,
                BlockedUsers = await _context.Users.CountAsync(u => u.IsBlocked)
            };
        }

        public async Task<PagedResult<ReportResponseDto>> GetReportsAsync(AdminReportFilterDto filter)
        {
            var query = _context.Reports
                .Include(r => r.ReportedBy)
                .Include(r => r.TargetAdvert)
                .Include(r => r.TargetUser)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.Status) && Enum.TryParse<ReportStatus>(filter.Status, true, out var status))
                query = query.Where(r => r.Status == status);

            if (!string.IsNullOrWhiteSpace(filter.TargetType) && Enum.TryParse<ReportTargetType>(filter.TargetType, true, out var targetType))
                query = query.Where(r => r.TargetType == targetType);

            if (!string.IsNullOrWhiteSpace(filter.Reason) && Enum.TryParse<ReportReason>(filter.Reason, true, out var reason))
                query = query.Where(r => r.Reason == reason);

            if (!string.IsNullOrWhiteSpace(filter.Search))
                query = query.Where(r =>
                    (r.ReportedBy.Name + " " + r.ReportedBy.Surname).Contains(filter.Search) ||
                    (r.Content != null && r.Content.Contains(filter.Search)));

            query = query.OrderByDescending(r => r.ReportedAt);

            var totalCount = await query.CountAsync();
            var items = await query.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToListAsync();

            return new PagedResult<ReportResponseDto>
            {
                TotalCount = totalCount,
                Items = items.Select(ReportService.MapToDto).ToList()
            };
        }

        public async Task<ReportResponseDto?> GetReportByIdAsync(int id)
        {
            var report = await _context.Reports
                .Include(r => r.ReportedBy)
                .Include(r => r.TargetAdvert)
                .Include(r => r.TargetUser)
                .FirstOrDefaultAsync(r => r.Id == id);
            return report == null ? null : ReportService.MapToDto(report);
        }

        public async Task ResolveReportAsync(int reportId, int adminUserId, string? note)
        {
            var report = await _context.Reports.FindAsync(reportId)
                ?? throw new KeyNotFoundException("Report not found");

            report.Status = ReportStatus.Resolved;
            report.ResolvedAt = DateTime.UtcNow;
            report.ResolvedByAdminId = adminUserId;
            report.AdminNote = note;

            _context.AdminActionLogs.Add(new AdminActionLog
            {
                AdminUserId = adminUserId,
                ActionType = AdminActionType.ResolveReport,
                ReportId = reportId,
                Note = note,
                PerformedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
        }

        public async Task RejectReportAsync(int reportId, int adminUserId, string? note)
        {
            var report = await _context.Reports.FindAsync(reportId)
                ?? throw new KeyNotFoundException("Report not found");

            report.Status = ReportStatus.Rejected;
            report.ResolvedAt = DateTime.UtcNow;
            report.ResolvedByAdminId = adminUserId;
            report.AdminNote = note;

            _context.AdminActionLogs.Add(new AdminActionLog
            {
                AdminUserId = adminUserId,
                ActionType = AdminActionType.RejectReport,
                ReportId = reportId,
                Note = note,
                PerformedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
        }

        public async Task HideAdvertAsync(int advertId, int adminUserId, string? note)
        {
            var advert = await _context.CarAdverts.FindAsync(advertId)
                ?? throw new KeyNotFoundException("Advert not found");

            advert.IsHidden = true;
            advert.UpdatedAt = DateTime.UtcNow;

            _context.AdminActionLogs.Add(new AdminActionLog
            {
                AdminUserId = adminUserId,
                ActionType = AdminActionType.HideAdvert,
                TargetAdvertId = advertId,
                Note = note,
                PerformedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
        }

        public async Task UnhideAdvertAsync(int advertId, int adminUserId)
        {
            var advert = await _context.CarAdverts.FindAsync(advertId)
                ?? throw new KeyNotFoundException("Advert not found");

            advert.IsHidden = false;
            advert.UpdatedAt = DateTime.UtcNow;

            _context.AdminActionLogs.Add(new AdminActionLog
            {
                AdminUserId = adminUserId,
                ActionType = AdminActionType.UnhideAdvert,
                TargetAdvertId = advertId,
                PerformedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
        }

        public async Task DeleteAdvertAsync(int advertId, int adminUserId, string? note)
        {
            var advert = await _context.CarAdverts.FindAsync(advertId)
                ?? throw new KeyNotFoundException("Advert not found");

            _context.AdminActionLogs.Add(new AdminActionLog
            {
                AdminUserId = adminUserId,
                ActionType = AdminActionType.DeleteAdvert,
                TargetAdvertId = advertId,
                Note = note,
                PerformedAt = DateTime.UtcNow
            });

            _context.CarAdverts.Remove(advert);
            await _context.SaveChangesAsync();
        }

        public async Task ActivateAdvertAsync(int advertId, int adminUserId)
        {
            var advert = await _context.CarAdverts.FindAsync(advertId)
                ?? throw new KeyNotFoundException("Advert not found");

            advert.IsActive = true;
            advert.UpdatedAt = DateTime.UtcNow;

            _context.AdminActionLogs.Add(new AdminActionLog
            {
                AdminUserId = adminUserId,
                ActionType = AdminActionType.ActivateAdvert,
                TargetAdvertId = advertId,
                PerformedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
        }

        public async Task DeactivateAdvertAsync(int advertId, int adminUserId)
        {
            var advert = await _context.CarAdverts.FindAsync(advertId)
                ?? throw new KeyNotFoundException("Advert not found");

            advert.IsActive = false;
            advert.UpdatedAt = DateTime.UtcNow;

            _context.AdminActionLogs.Add(new AdminActionLog
            {
                AdminUserId = adminUserId,
                ActionType = AdminActionType.DeactivateAdvert,
                TargetAdvertId = advertId,
                PerformedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
        }

        public async Task BlockUserAsync(int userId, int adminUserId, string? reason)
        {
            var user = await _context.Users.FindAsync(userId)
                ?? throw new KeyNotFoundException("User not found");

            user.IsBlocked = true;
            user.BlockedAt = DateTime.UtcNow;
            user.BlockedReason = reason;

            _context.AdminActionLogs.Add(new AdminActionLog
            {
                AdminUserId = adminUserId,
                ActionType = AdminActionType.BlockUser,
                TargetUserId = userId,
                Note = reason,
                PerformedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
        }

        public async Task UnblockUserAsync(int userId, int adminUserId)
        {
            var user = await _context.Users.FindAsync(userId)
                ?? throw new KeyNotFoundException("User not found");

            user.IsBlocked = false;
            user.BlockedAt = null;
            user.BlockedReason = null;

            _context.AdminActionLogs.Add(new AdminActionLog
            {
                AdminUserId = adminUserId,
                ActionType = AdminActionType.UnblockUser,
                TargetUserId = userId,
                PerformedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
        }

        public async Task<PagedResult<AdminUserDto>> GetUsersAsync(string? search, int page, int pageSize)
        {
            var query = _context.Users
                .Include(u => u.Adverts)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(u =>
                    u.Email.Contains(search) ||
                    (u.Name + " " + u.Surname).Contains(search));

            query = query.OrderBy(u => u.Id);

            var totalCount = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return new PagedResult<AdminUserDto>
            {
                TotalCount = totalCount,
                Items = items.Select(u => new AdminUserDto
                {
                    Id = u.Id,
                    Name = u.Name,
                    Surname = u.Surname,
                    Email = u.Email,
                    PhoneNumber = u.PhoneNumber,
                    IsAdmin = u.IsAdmin,
                    IsBlocked = u.IsBlocked,
                    BlockedAt = u.BlockedAt,
                    BlockedReason = u.BlockedReason,
                    AdvertCount = u.Adverts.Count
                }).ToList()
            };
        }

        public async Task<PagedResult<AdminAdvertDto>> GetAdvertsAsync(string? search, bool? isHidden, bool? isActive, int page, int pageSize)
        {
            var query = _context.CarAdverts
                .Include(a => a.createdBy)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(a => a.Title.Contains(search));

            if (isHidden.HasValue)
                query = query.Where(a => a.IsHidden == isHidden);

            if (isActive.HasValue)
                query = query.Where(a => a.IsActive == isActive);

            query = query.OrderByDescending(a => a.CreatedAt);

            var totalCount = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return new PagedResult<AdminAdvertDto>
            {
                TotalCount = totalCount,
                Items = items.Select(a => new AdminAdvertDto
                {
                    Id = a.Id,
                    Title = a.Title,
                    Price = a.Price,
                    Currency = a.Currency,
                    IsHidden = a.IsHidden,
                    IsActive = a.IsActive,
                    CreatedAt = a.CreatedAt,
                    UserId = a.UserId,
                    OwnerName = $"{a.createdBy.Name} {a.createdBy.Surname}",
                    City = a.City,
                    Region = a.Region
                }).ToList()
            };
        }

        public async Task<List<AdminActionLogDto>> GetActionLogsAsync(int page, int pageSize)
        {
            var logs = await _context.AdminActionLogs
                .Include(l => l.Admin)
                .OrderByDescending(l => l.PerformedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return logs.Select(l => new AdminActionLogDto
            {
                Id = l.Id,
                AdminUserId = l.AdminUserId,
                AdminName = $"{l.Admin.Name} {l.Admin.Surname}",
                ActionType = l.ActionType.ToString(),
                TargetAdvertId = l.TargetAdvertId,
                TargetUserId = l.TargetUserId,
                ReportId = l.ReportId,
                Note = l.Note,
                PerformedAt = l.PerformedAt
            }).ToList();
        }
    }
}