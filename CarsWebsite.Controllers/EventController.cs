using cars_website_api.CarsWebsite.DTOs.Event;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
public class EventController : ControllerBase
{
    private readonly IEventService _eventService;
    private readonly AppDbContext _context;

    public EventController(IEventService eventService, AppDbContext context)
    {
        _eventService = eventService;
        _context = context;
    }

    private int? GetUserId()
    {
        var s = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(s, out var id) ? id : null;
    }

    private async Task<bool> IsAdminAsync()
    {
        var id = GetUserId();
        if (id == null) return false;
        var user = await _context.Users.FindAsync(id);
        return user?.IsAdmin == true;
    }

    [HttpGet]
    public async Task<IActionResult> GetEvents(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12)
        => Ok(await _eventService.GetPublishedEventsAsync(search, page, pageSize));

    [HttpGet("upcoming")]
    public async Task<IActionResult> GetUpcoming([FromQuery] int count = 4)
        => Ok(await _eventService.GetUpcomingEventsAsync(count));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetEvent(int id)
    {
        var ev = await _eventService.GetEventByIdAsync(id);
        if (ev == null) return NotFound();
        return Ok(ev);
    }

    [HttpGet("my")]
    [Authorize]
    public async Task<IActionResult> GetMyEvents(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();
        return Ok(await _eventService.GetMyEventsAsync(userId.Value, page, pageSize));
    }

    [HttpPost]
    [Authorize]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> CreateEvent([FromForm] CreateEventDto dto,
        [FromForm] IFormFile? mainImage,
        [FromForm] List<IFormFile>? galleryImages)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var result = await _eventService.CreateEventAsync(dto, userId.Value, mainImage, galleryImages);
        return CreatedAtAction(nameof(GetEvent), new { id = result.Id }, result);
    }

    [HttpPost("{id}/report")]
    [Authorize]
    public async Task<IActionResult> ReportEvent(int id, [FromBody] CreateEventReportDto dto)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        await _eventService.ReportEventAsync(id, userId.Value, dto);
        return NoContent();
    }

    [HttpPost("{id}/attend")]
    [Authorize]
    public async Task<IActionResult> Attend(int id)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();
        await _eventService.AttendEventAsync(id, userId.Value);
        return NoContent();
    }

    [HttpDelete("{id}/attend")]
    [Authorize]
    public async Task<IActionResult> Unattend(int id)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();
        await _eventService.UnattendEventAsync(id, userId.Value);
        return NoContent();
    }

    [HttpPost("{id}/favourite")]
    [Authorize]
    public async Task<IActionResult> Favourite(int id)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();
        await _eventService.FavouriteEventAsync(id, userId.Value);
        return NoContent();
    }

    [HttpDelete("{id}/favourite")]
    [Authorize]
    public async Task<IActionResult> Unfavourite(int id)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();
        await _eventService.UnfavouriteEventAsync(id, userId.Value);
        return NoContent();
    }

    // ── Admin endpoints ────────────────────────────────────────────────────────

    [HttpGet("admin/all")]
    [Authorize]
    public async Task<IActionResult> AdminGetEvents([FromQuery] AdminEventFilterDto filter)
    {
        if (!await IsAdminAsync()) return Forbid();
        return Ok(await _eventService.GetAdminEventsAsync(filter));
    }

    [HttpGet("admin/{id}")]
    [Authorize]
    public async Task<IActionResult> AdminGetEvent(int id)
    {
        if (!await IsAdminAsync()) return Forbid();
        var ev = await _eventService.GetAdminEventByIdAsync(id);
        if (ev == null) return NotFound();
        return Ok(ev);
    }

    [HttpPost("admin/{id}/publish")]
    [Authorize]
    public async Task<IActionResult> PublishEvent(int id)
    {
        if (!await IsAdminAsync()) return Forbid();
        await _eventService.PublishEventAsync(id, GetUserId()!.Value);
        return NoContent();
    }

    [HttpPost("admin/{id}/reject")]
    [Authorize]
    public async Task<IActionResult> RejectEvent(int id, [FromBody] AdminEventActionDto dto)
    {
        if (!await IsAdminAsync()) return Forbid();
        await _eventService.RejectEventAsync(id, GetUserId()!.Value, dto.Note);
        return NoContent();
    }

    [HttpPost("admin/{id}/archive")]
    [Authorize]
    public async Task<IActionResult> ArchiveEvent(int id)
    {
        if (!await IsAdminAsync()) return Forbid();
        await _eventService.ArchiveEventAsync(id, GetUserId()!.Value);
        return NoContent();
    }

    [HttpPost("admin/{id}/feature")]
    [Authorize]
    public async Task<IActionResult> FeatureEvent(int id)
    {
        if (!await IsAdminAsync()) return Forbid();
        await _eventService.FeatureEventAsync(id, GetUserId()!.Value, true);
        return NoContent();
    }

    [HttpDelete("admin/{id}/feature")]
    [Authorize]
    public async Task<IActionResult> UnfeatureEvent(int id)
    {
        if (!await IsAdminAsync()) return Forbid();
        await _eventService.FeatureEventAsync(id, GetUserId()!.Value, false);
        return NoContent();
    }

    [HttpDelete("admin/{id}")]
    [Authorize]
    public async Task<IActionResult> DeleteEvent(int id)
    {
        if (!await IsAdminAsync()) return Forbid();
        await _eventService.DeleteEventAsync(id, GetUserId()!.Value);
        return NoContent();
    }

    [HttpPut("admin/{id}")]
    [Authorize]
    public async Task<IActionResult> UpdateEvent(int id, [FromBody] CreateEventDto dto)
    {
        if (!await IsAdminAsync()) return Forbid();
        var result = await _eventService.UpdateEventAsync(id, dto, GetUserId()!.Value);
        return Ok(result);
    }
}

public class AdminEventActionDto
{
    public string? Note { get; set; }
}
