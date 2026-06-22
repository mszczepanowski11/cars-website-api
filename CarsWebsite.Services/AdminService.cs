using cars_website_api.CarsWebsite.DTOs.Admin;
using cars_website_api.CarsWebsite.DTOs.Report;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Services
{
    public class AdminService : IAdminService
    {
        private readonly AppDbContext _context;
        private readonly Cloudinary _cloudinary;

        public AdminService(AppDbContext context, Cloudinary cloudinary)
        {
            _context = context;
            _cloudinary = cloudinary;
        }

        public async Task<AdminStatsDto> GetStatsAsync()
        {
            var now = DateTime.UtcNow;
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEnd = monthStart.AddMonths(1);
            return new AdminStatsDto
            {
                TotalActiveAdverts = await _context.CarAdverts.CountAsync(a => a.IsActive && !a.IsHidden),
                TotalUsers = await _context.Users.CountAsync(),
                TotalReports = await _context.Reports.CountAsync(),
                PendingReports = await _context.Reports.CountAsync(r => r.Status == ReportStatus.Pending),
                NewRegistrationsThisMonth = await _context.Users.CountAsync(u => u.CreatedAt >= monthStart && u.CreatedAt < monthEnd),
                BlockedUsers = await _context.Users.CountAsync(u => u.IsBlocked)
            };
        }

        public async Task<PagedResult<ReportResponseDto>> GetReportsAsync(AdminReportFilterDto filter)
        {
            filter.PageSize = Math.Clamp(filter.PageSize, 1, 100);
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
                .Include(r => r.ReportedBy).Include(r => r.TargetAdvert).Include(r => r.TargetUser)
                .FirstOrDefaultAsync(r => r.Id == id);
            return report == null ? null : ReportService.MapToDto(report);
        }

        public async Task ResolveReportAsync(int reportId, int adminUserId, string? note)
        {
            var report = await _context.Reports.FindAsync(reportId) ?? throw new KeyNotFoundException("Report not found");
            report.Status = ReportStatus.Resolved;
            report.ResolvedAt = DateTime.UtcNow;
            report.ResolvedByAdminId = adminUserId;
            report.AdminNote = note;
            _context.AdminActionLogs.Add(new AdminActionLog { AdminUserId = adminUserId, ActionType = AdminActionType.ResolveReport, ReportId = reportId, Note = note, PerformedAt = DateTime.UtcNow });
            await _context.SaveChangesAsync();
        }

        public async Task RejectReportAsync(int reportId, int adminUserId, string? note)
        {
            var report = await _context.Reports.FindAsync(reportId) ?? throw new KeyNotFoundException("Report not found");
            report.Status = ReportStatus.Rejected;
            report.ResolvedAt = DateTime.UtcNow;
            report.ResolvedByAdminId = adminUserId;
            report.AdminNote = note;
            _context.AdminActionLogs.Add(new AdminActionLog { AdminUserId = adminUserId, ActionType = AdminActionType.RejectReport, ReportId = reportId, Note = note, PerformedAt = DateTime.UtcNow });
            await _context.SaveChangesAsync();
        }

        public async Task HideAdvertAsync(int advertId, int adminUserId, string? note)
        {
            var advert = await _context.CarAdverts.FindAsync(advertId) ?? throw new KeyNotFoundException("Advert not found");
            advert.IsHidden = true; advert.UpdatedAt = DateTime.UtcNow;
            _context.AdminActionLogs.Add(new AdminActionLog { AdminUserId = adminUserId, ActionType = AdminActionType.HideAdvert, TargetAdvertId = advertId, Note = note, PerformedAt = DateTime.UtcNow });
            await _context.SaveChangesAsync();
        }

        public async Task UnhideAdvertAsync(int advertId, int adminUserId)
        {
            var advert = await _context.CarAdverts.FindAsync(advertId) ?? throw new KeyNotFoundException("Advert not found");
            advert.IsHidden = false; advert.UpdatedAt = DateTime.UtcNow;
            _context.AdminActionLogs.Add(new AdminActionLog { AdminUserId = adminUserId, ActionType = AdminActionType.UnhideAdvert, TargetAdvertId = advertId, PerformedAt = DateTime.UtcNow });
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAdvertAsync(int advertId, int adminUserId, string? note)
        {
            var advert = await _context.CarAdverts
                .Include(a => a.Images)
                .FirstOrDefaultAsync(a => a.Id == advertId)
                ?? throw new KeyNotFoundException("Advert not found");

            foreach (var image in advert.Images)
            {
                var publicId = ExtractPublicId(image.Url);
                if (publicId != null)
                {
                    try { await _cloudinary.DestroyAsync(new DeletionParams(publicId)); }
                    catch { /* best-effort cleanup */ }
                }
            }
            _context.AdvertImages.RemoveRange(advert.Images);

            _context.AdminActionLogs.Add(new AdminActionLog { AdminUserId = adminUserId, ActionType = AdminActionType.DeleteAdvert, TargetAdvertId = advertId, Note = note, PerformedAt = DateTime.UtcNow });
            advert.IsHidden = true;
            advert.IsActive = false;
            advert.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        public async Task ActivateAdvertAsync(int advertId, int adminUserId)
        {
            var advert = await _context.CarAdverts.FindAsync(advertId) ?? throw new KeyNotFoundException("Advert not found");
            advert.IsActive = true;
            advert.ExpiresAt = DateTime.UtcNow.AddDays(30);
            advert.UpdatedAt = DateTime.UtcNow;
            _context.AdminActionLogs.Add(new AdminActionLog { AdminUserId = adminUserId, ActionType = AdminActionType.ActivateAdvert, TargetAdvertId = advertId, PerformedAt = DateTime.UtcNow });
            await _context.SaveChangesAsync();
        }

        public async Task DeactivateAdvertAsync(int advertId, int adminUserId)
        {
            var advert = await _context.CarAdverts.FindAsync(advertId) ?? throw new KeyNotFoundException("Advert not found");
            advert.IsActive = false; advert.UpdatedAt = DateTime.UtcNow;
            _context.AdminActionLogs.Add(new AdminActionLog { AdminUserId = adminUserId, ActionType = AdminActionType.DeactivateAdvert, TargetAdvertId = advertId, PerformedAt = DateTime.UtcNow });
            await _context.SaveChangesAsync();
        }

        public async Task BlockUserAsync(int userId, int adminUserId, string? reason)
        {
            var user = await _context.Users.FindAsync(userId) ?? throw new KeyNotFoundException("User not found");
            user.IsBlocked = true; user.BlockedAt = DateTime.UtcNow; user.BlockedReason = reason;
            _context.AdminActionLogs.Add(new AdminActionLog { AdminUserId = adminUserId, ActionType = AdminActionType.BlockUser, TargetUserId = userId, Note = reason, PerformedAt = DateTime.UtcNow });
            await _context.SaveChangesAsync();
        }

        public async Task UnblockUserAsync(int userId, int adminUserId)
        {
            var user = await _context.Users.FindAsync(userId) ?? throw new KeyNotFoundException("User not found");
            user.IsBlocked = false; user.BlockedAt = null; user.BlockedReason = null;
            _context.AdminActionLogs.Add(new AdminActionLog { AdminUserId = adminUserId, ActionType = AdminActionType.UnblockUser, TargetUserId = userId, PerformedAt = DateTime.UtcNow });
            await _context.SaveChangesAsync();
        }

        public async Task DeleteUserAsync(int userId, int adminUserId, string? note)
        {
            if (userId == adminUserId)
                throw new InvalidOperationException("Admin nie może usunąć własnego konta z panelu administracyjnego.");
            var user = await _context.Users.FindAsync(userId)
                ?? throw new KeyNotFoundException("Użytkownik nie istnieje.");
            if (user.IsAdmin)
                throw new InvalidOperationException("Nie można usunąć konta administratora.");

            // Anonymize for RODO compliance instead of hard delete
            user.Name = "Usunięty";
            user.Surname = "Użytkownik";
            user.Email = $"deleted_{userId}@carizo.deleted";
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString());
            user.PhoneNumber = null;
            user.AvatarUrl = null;
            user.About = null;
            user.City = null;
            user.Region = null;
            user.Street = null;
            user.PostalCode = null;
            user.CompanyName = null;
            user.Nip = null;
            user.IsBlocked = true;
            user.BlockedReason = "Konto usunięte przez administratora";
            user.PasswordResetToken = null;
            user.PasswordResetTokenExpires = null;
            user.EmailVerificationToken = null;
            user.EmailVerificationTokenExpires = null;

            // Soft-delete all adverts belonging to this user
            var adverts = await _context.CarAdverts.Where(a => a.UserId == userId).ToListAsync();
            foreach (var advert in adverts)
            {
                advert.IsActive = false;
                advert.IsHidden = true;
                advert.UpdatedAt = DateTime.UtcNow;
            }

            _context.AdminActionLogs.Add(new AdminActionLog
            {
                AdminUserId = adminUserId,
                ActionType = AdminActionType.DeleteUser,
                TargetUserId = userId,
                Note = note,
                PerformedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
        }

        public async Task<PagedResult<AdminUserDto>> GetUsersAsync(string? search, int page, int pageSize)
        {
            pageSize = Math.Clamp(pageSize, 1, 100);
            var query = _context.Users.Include(u => u.Adverts).AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(u => u.Email.Contains(search) || (u.Name + " " + u.Surname).Contains(search));
            query = query.OrderBy(u => u.Id);
            var totalCount = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return new PagedResult<AdminUserDto>
            {
                TotalCount = totalCount,
                Items = items.Select(u => new AdminUserDto
                {
                    Id = u.Id, Name = u.Name, Surname = u.Surname, Email = u.Email,
                    PhoneNumber = u.PhoneNumber, IsAdmin = u.IsAdmin, IsBlocked = u.IsBlocked,
                    BlockedAt = u.BlockedAt, BlockedReason = u.BlockedReason, AdvertCount = u.Adverts.Count
                }).ToList()
            };
        }

        public async Task<PagedResult<AdminAdvertDto>> GetAdvertsAsync(string? search, bool? isHidden, bool? isActive, int page, int pageSize)
        {
            pageSize = Math.Clamp(pageSize, 1, 100);
            var query = _context.CarAdverts
                .Include(a => a.createdBy)
                .Include(a => a.Brand)
                .Include(a => a.Model)
                .Include(a => a.Images)
                .AsQueryable();
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(a => a.Title.Contains(search));
            if (isHidden.HasValue) query = query.Where(a => a.IsHidden == isHidden);
            if (isActive.HasValue) query = query.Where(a => a.IsActive == isActive);
            query = query.OrderByDescending(a => a.CreatedAt);
            var totalCount = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var advertIds = items.Select(a => a.Id).ToList();
            var viewCounts = await _context.AdvertViews
                .Where(v => advertIds.Contains(v.AdvertId))
                .GroupBy(v => v.AdvertId)
                .Select(g => new { AdvertId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.AdvertId, g => g.Count);

            return new PagedResult<AdminAdvertDto>
            {
                TotalCount = totalCount,
                Items = items.Select(a => new AdminAdvertDto
                {
                    Id = a.Id, Title = a.Title, Price = a.Price, Currency = a.Currency,
                    IsHidden = a.IsHidden, IsActive = a.IsActive, CreatedAt = a.CreatedAt,
                    UserId = a.UserId, OwnerName = $"{a.createdBy.Name} {a.createdBy.Surname}",
                    City = a.City, Region = a.Region,
                    Brand = a.Brand?.Name, Model = a.Model?.Name, Year = a.Year,
                    Badge = a.Badge, SoldAt = a.SoldAt,
                    MainImageUrl = a.Images?.FirstOrDefault(i => i.IsMain)?.Url ?? a.Images?.FirstOrDefault()?.Url,
                    ViewCount = viewCounts.GetValueOrDefault(a.Id, 0)
                }).ToList()
            };
        }

        public async Task<List<AdminActionLogDto>> GetActionLogsAsync(int page, int pageSize)
        {
            pageSize = Math.Clamp(pageSize, 1, 100);
            var logs = await _context.AdminActionLogs
                .Include(l => l.Admin)
                .OrderByDescending(l => l.PerformedAt)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return logs.Select(l => new AdminActionLogDto
            {
                Id = l.Id, AdminUserId = l.AdminUserId, AdminName = $"{l.Admin.Name} {l.Admin.Surname}",
                ActionType = l.ActionType.ToString(), TargetAdvertId = l.TargetAdvertId,
                TargetUserId = l.TargetUserId, ReportId = l.ReportId, Note = l.Note, PerformedAt = l.PerformedAt
            }).ToList();
        }

        private static string? ExtractPublicId(string url)
        {
            try
            {
                var segments = new Uri(url).AbsolutePath.Split('/');
                var uploadIdx = Array.IndexOf(segments, "upload");
                if (uploadIdx < 0) return null;
                var start = uploadIdx + 1;
                if (start < segments.Length && segments[start].StartsWith('v') && long.TryParse(segments[start][1..], out _))
                    start++;
                var idWithExt = string.Join("/", segments[start..]);
                var dot = idWithExt.LastIndexOf('.');
                return dot > 0 ? idWithExt[..dot] : idWithExt;
            }
            catch { return null; }
        }
    }
}