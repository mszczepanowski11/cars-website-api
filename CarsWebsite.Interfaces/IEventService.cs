using cars_website_api.CarsWebsite.DTOs.Event;
using CarsWebsite;

namespace cars_website_api.CarsWebsite.Interfaces;

public interface IEventService
{
    Task<PagedResult<EventResponseDto>> GetPublishedEventsAsync(string? search, int page, int pageSize);
    Task<List<EventResponseDto>> GetUpcomingEventsAsync(int count);
    Task<EventResponseDto?> GetEventByIdAsync(int id);
    Task<EventResponseDto> CreateEventAsync(CreateEventDto dto, int userId, IFormFile? mainImage, List<IFormFile>? galleryImages);
    Task ReportEventAsync(int eventId, int userId, CreateEventReportDto dto);
    Task<PagedResult<AdminEventDto>> GetAdminEventsAsync(AdminEventFilterDto filter);
    Task<EventResponseDto?> GetAdminEventByIdAsync(int id);
    Task PublishEventAsync(int id, int adminId);
    Task RejectEventAsync(int id, int adminId, string? note);
    Task ArchiveEventAsync(int id, int adminId);
    Task DeleteEventAsync(int id, int adminId);
    Task<EventResponseDto> UpdateEventAsync(int id, CreateEventDto dto, int adminId);
    Task FeatureEventAsync(int id, int adminId, bool featured);

    // User actions
    Task AttendEventAsync(int eventId, int userId);
    Task UnattendEventAsync(int eventId, int userId);
    Task FavouriteEventAsync(int eventId, int userId);
    Task UnfavouriteEventAsync(int eventId, int userId);
    Task<PagedResult<EventResponseDto>> GetMyEventsAsync(int userId, int page, int pageSize);
}
