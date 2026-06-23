using cars_website_api.CarsWebsite.DTOs.Event;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Services;

public class EventService : IEventService
{
    private readonly AppDbContext _context;
    private readonly Cloudinary _cloudinary;

    public EventService(AppDbContext context, Cloudinary cloudinary)
    {
        _context = context;
        _cloudinary = cloudinary;
    }

    private static readonly string[] AllowedMimeTypes = ["image/jpeg", "image/png", "image/webp"];
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    private async Task<string> SaveImageAsync(IFormFile file, int eventId)
    {
        var mime = file.ContentType.ToLowerInvariant();
        if (!AllowedMimeTypes.Contains(mime))
            throw new BadHttpRequestException("Invalid file type.");
        if (file.Length > MaxFileSizeBytes)
            throw new BadHttpRequestException("File too large (max 10 MB).");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
        {
            ext = mime switch
            {
                "image/jpeg" => ".jpg",
                "image/png"  => ".png",
                "image/webp" => ".webp",
                _ => throw new BadHttpRequestException("Invalid file extension.")
            };
        }

        var publicId = $"events/{eventId}/{Guid.NewGuid()}";

        ImageUploadResult result;
        using (var stream = file.OpenReadStream())
        {
            result = await _cloudinary.UploadAsync(new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                PublicId = publicId,
                Overwrite = false,
                Transformation = new Transformation().Quality("auto"),
            });
        }

        if (result.Error != null)
            throw new InvalidOperationException($"Błąd Cloudinary: {result.Error.Message}");

        return result.SecureUrl.ToString();
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

    private static EventResponseDto MapToDto(global::CarsWebsite.Event e, int attendingCount = 0, int interestedCount = 0, bool isUserInterested = false, bool isUserFavorite = false) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Description = e.Description,
        StartDate = e.StartDate,
        EndDate = e.EndDate,
        City = e.City,
        Address = e.Address,
        WebsiteUrl = e.WebsiteUrl,
        TicketsUrl = e.TicketsUrl,
        OrganizerName = e.OrganizerName,
        OrganizerEmail = e.OrganizerEmail,
        OrganizerPhone = e.OrganizerPhone,
        Status = e.Status.ToString(),
        IsFeatured = e.IsFeatured,
        CreatedAt = e.CreatedAt,
        CreatedByUserId = e.CreatedByUserId,
        CreatedByName = e.CreatedBy != null ? $"{e.CreatedBy.Name} {e.CreatedBy.Surname}" : null,
        Images = e.Images.Select(i => new EventImageDto { Id = i.Id, Url = i.Url, IsMain = i.IsMain }).ToList(),
        AttendingCount = attendingCount,
        InterestedCount = interestedCount,
        IsUserInterested = isUserInterested,
        IsUserFavorite = isUserFavorite
    };

    public async Task<PagedResult<EventResponseDto>> GetPublishedEventsAsync(string? search, int page, int pageSize)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _context.Events
            .Include(e => e.Images)
            .Include(e => e.CreatedBy)
            .Where(e => e.Status == EventStatus.Published)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(e => e.Name.Contains(search) || e.City.Contains(search) || e.Description.Contains(search));

        query = query.OrderBy(e => e.StartDate);

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<EventResponseDto>
        {
            Items = items.Select(e => MapToDto(e)).ToList(),
            TotalCount = total
        };
    }

    public async Task<List<EventResponseDto>> GetUpcomingEventsAsync(int count)
    {
        var now = DateTime.UtcNow;
        var items = await _context.Events
            .Include(e => e.Images)
            .Include(e => e.CreatedBy)
            .Where(e => e.Status == EventStatus.Published && e.StartDate >= now)
            .OrderBy(e => e.StartDate)
            .Take(count)
            .ToListAsync();

        return items.Select(e => MapToDto(e)).ToList();
    }

    public async Task<EventResponseDto?> GetEventByIdAsync(int id)
    {
        var e = await _context.Events
            .Include(e => e.Images)
            .Include(e => e.CreatedBy)
            .FirstOrDefaultAsync(e => e.Id == id && e.Status == EventStatus.Published);

        if (e == null) return null;

        var attendingCount = await _context.EventAttendees.CountAsync(a => a.EventId == id);
        var interestedCount = await _context.EventFavourites.CountAsync(f => f.EventId == id);

        return MapToDto(e, attendingCount, interestedCount);
    }

    public async Task<EventResponseDto> CreateEventAsync(CreateEventDto dto, int userId, IFormFile? mainImage, List<IFormFile>? galleryImages)
    {
        var ev = new global::CarsWebsite.Event
        {
            Name = dto.Name,
            Description = dto.Description,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            City = dto.City,
            Address = dto.Address,
            WebsiteUrl = dto.WebsiteUrl,
            TicketsUrl = dto.TicketsUrl,
            OrganizerName = dto.OrganizerName,
            OrganizerEmail = dto.OrganizerEmail,
            OrganizerPhone = dto.OrganizerPhone,
            Status = EventStatus.Pending,
            CreatedByUserId = userId
        };

        _context.Events.Add(ev);
        await _context.SaveChangesAsync();

        if (mainImage != null)
        {
            var url = await SaveImageAsync(mainImage, ev.Id);
            _context.EventImages.Add(new EventImage { EventId = ev.Id, Url = url, IsMain = true });
        }

        if (galleryImages != null)
        {
            foreach (var img in galleryImages.Take(10))
            {
                var url = await SaveImageAsync(img, ev.Id);
                _context.EventImages.Add(new EventImage { EventId = ev.Id, Url = url, IsMain = false });
            }
        }

        await _context.SaveChangesAsync();

        var created = await _context.Events
            .Include(e => e.Images)
            .Include(e => e.CreatedBy)
            .FirstAsync(e => e.Id == ev.Id);

        return MapToDto(created);
    }

    public async Task ReportEventAsync(int eventId, int userId, CreateEventReportDto dto)
    {
        var ev = await _context.Events.FindAsync(eventId)
            ?? throw new KeyNotFoundException("Event not found.");

        if (ev.Status != EventStatus.Published)
            throw new BadHttpRequestException("Cannot report this event.");

        var existing = await _context.EventReports
            .AnyAsync(r => r.EventId == eventId && r.ReportedByUserId == userId && !r.IsResolved);
        if (existing)
            throw new BadHttpRequestException("You already reported this event.");

        _context.EventReports.Add(new EventReport
        {
            EventId = eventId,
            ReportedByUserId = userId,
            Reason = dto.Reason,
            Content = dto.Content
        });
        await _context.SaveChangesAsync();
    }

    public async Task<PagedResult<AdminEventDto>> GetAdminEventsAsync(AdminEventFilterDto filter)
    {
        var query = _context.Events
            .Include(e => e.CreatedBy)
            .Include(e => e.Images)
            .Include(e => e.Reports)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Status) && Enum.TryParse<EventStatus>(filter.Status, true, out var status))
            query = query.Where(e => e.Status == status);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(e => e.Name.Contains(filter.Search) || e.City.Contains(filter.Search));

        query = query.OrderByDescending(e => e.CreatedAt);

        var total = await query.CountAsync();
        var items = await query.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToListAsync();

        return new PagedResult<AdminEventDto>
        {
            Items = items.Select(e => new AdminEventDto
            {
                Id = e.Id,
                Name = e.Name,
                StartDate = e.StartDate,
                EndDate = e.EndDate,
                City = e.City,
                Status = e.Status.ToString(),
                CreatedAt = e.CreatedAt,
                CreatedByUserId = e.CreatedByUserId,
                CreatedByName = e.CreatedBy != null ? $"{e.CreatedBy.Name} {e.CreatedBy.Surname}" : null,
                ReportCount = e.Reports.Count(r => !r.IsResolved),
                MainImageUrl = e.Images.FirstOrDefault(i => i.IsMain)?.Url ?? e.Images.FirstOrDefault()?.Url,
                IsFeatured = e.IsFeatured
            }).ToList(),
            TotalCount = total
        };
    }

    public async Task<EventResponseDto?> GetAdminEventByIdAsync(int id)
    {
        var e = await _context.Events
            .Include(e => e.Images)
            .Include(e => e.CreatedBy)
            .FirstOrDefaultAsync(e => e.Id == id);

        return e == null ? null : MapToDto(e);
    }

    public async Task PublishEventAsync(int id, int adminId)
    {
        var ev = await _context.Events.FindAsync(id)
            ?? throw new KeyNotFoundException("Event not found.");
        ev.Status = EventStatus.Published;
        ev.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task RejectEventAsync(int id, int adminId, string? note)
    {
        var ev = await _context.Events.FindAsync(id)
            ?? throw new KeyNotFoundException("Event not found.");
        ev.Status = EventStatus.Rejected;
        ev.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task ArchiveEventAsync(int id, int adminId)
    {
        var ev = await _context.Events.FindAsync(id)
            ?? throw new KeyNotFoundException("Event not found.");
        ev.Status = EventStatus.Archived;
        ev.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task DeleteEventAsync(int id, int adminId)
    {
        var ev = await _context.Events
            .Include(e => e.Images)
            .FirstOrDefaultAsync(e => e.Id == id)
            ?? throw new KeyNotFoundException("Event not found.");

        foreach (var img in ev.Images)
        {
            var publicId = ExtractPublicId(img.Url);
            if (publicId != null)
                await _cloudinary.DestroyAsync(new DeletionParams(publicId));
        }

        _context.Events.Remove(ev);
        await _context.SaveChangesAsync();
    }

    public async Task<EventResponseDto> UpdateEventAsync(int id, CreateEventDto dto, int adminId)
    {
        var ev = await _context.Events
            .Include(e => e.Images)
            .Include(e => e.CreatedBy)
            .FirstOrDefaultAsync(e => e.Id == id)
            ?? throw new KeyNotFoundException("Event not found.");

        ev.Name = dto.Name;
        ev.Description = dto.Description;
        ev.StartDate = dto.StartDate;
        ev.EndDate = dto.EndDate;
        ev.City = dto.City;
        ev.Address = dto.Address;
        ev.WebsiteUrl = dto.WebsiteUrl;
        ev.TicketsUrl = dto.TicketsUrl;
        ev.OrganizerName = dto.OrganizerName;
        ev.OrganizerEmail = dto.OrganizerEmail;
        ev.OrganizerPhone = dto.OrganizerPhone;
        ev.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return MapToDto(ev);
    }

    public async Task FeatureEventAsync(int id, int adminId, bool featured)
    {
        var ev = await _context.Events.FindAsync(id)
            ?? throw new KeyNotFoundException("Event not found.");
        ev.IsFeatured = featured;
        ev.UpdatedAt = DateTime.UtcNow;
        if (featured)
        {
            if (ev.FeaturedUntil == null || ev.FeaturedUntil < DateTime.UtcNow)
                ev.FeaturedUntil = DateTime.UtcNow.AddDays(30);
        }
        else
        {
            ev.FeaturedUntil = null;
        }
        await _context.SaveChangesAsync();
    }

    public async Task AttendEventAsync(int eventId, int userId)
    {
        var exists = await _context.EventAttendees.AnyAsync(a => a.EventId == eventId && a.UserId == userId);
        if (exists) return;
        _context.EventAttendees.Add(new EventAttendee { EventId = eventId, UserId = userId });
        await _context.SaveChangesAsync();
    }

    public async Task UnattendEventAsync(int eventId, int userId)
    {
        var a = await _context.EventAttendees.FirstOrDefaultAsync(a => a.EventId == eventId && a.UserId == userId);
        if (a == null) return;
        _context.EventAttendees.Remove(a);
        await _context.SaveChangesAsync();
    }

    public async Task FavouriteEventAsync(int eventId, int userId)
    {
        var exists = await _context.EventFavourites.AnyAsync(f => f.EventId == eventId && f.UserId == userId);
        if (exists) return;
        _context.EventFavourites.Add(new EventFavourite { EventId = eventId, UserId = userId });
        await _context.SaveChangesAsync();
    }

    public async Task UnfavouriteEventAsync(int eventId, int userId)
    {
        var f = await _context.EventFavourites.FirstOrDefaultAsync(f => f.EventId == eventId && f.UserId == userId);
        if (f == null) return;
        _context.EventFavourites.Remove(f);
        await _context.SaveChangesAsync();
    }

    public async Task<PagedResult<EventResponseDto>> GetMyEventsAsync(int userId, int page, int pageSize)
    {
        var query = _context.Events
            .Include(e => e.Images)
            .Include(e => e.CreatedBy)
            .Where(e => e.CreatedByUserId == userId)
            .OrderByDescending(e => e.CreatedAt);

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<EventResponseDto>
        {
            Items = items.Select(e => MapToDto(e)).ToList(),
            TotalCount = total
        };
    }
}
