using cars_website_api.CarsWebsite.DTOs.Event;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("global")]
public class EventController : ControllerBase
{
    private readonly IEventService _eventService;

    public EventController(IEventService eventService)
    {
        _eventService = eventService;
    }

    private int? GetUserId()
    {
        var s = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(s, out var id) ? id : null;
    }

    private bool IsAdmin() => User.FindFirstValue("isAdmin") == "true";

    [HttpGet]
    public async Task<IActionResult> GetEvents(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        if (search?.Length > 100) search = search[..100];
        return Ok(await _eventService.GetPublishedEventsAsync(search, page, pageSize));
    }

    [HttpGet("upcoming")]
    public async Task<IActionResult> GetUpcoming([FromQuery] int count = 4)
        => Ok(await _eventService.GetUpcomingEventsAsync(Math.Clamp(count, 1, 50)));

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
        return Ok(await _eventService.GetMyEventsAsync(userId.Value, Math.Max(1, page), Math.Clamp(pageSize, 1, 100)));
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
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AdminGetEvents([FromQuery] AdminEventFilterDto filter)
        => Ok(await _eventService.GetAdminEventsAsync(filter));

    [HttpGet("admin/{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AdminGetEvent(int id)
    {
        var ev = await _eventService.GetAdminEventByIdAsync(id);
        if (ev == null) return NotFound();
        return Ok(ev);
    }

    [HttpPost("admin/{id}/publish")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> PublishEvent(int id)
    {
        var adminId = GetUserId();
        if (adminId == null) return Unauthorized();
        await _eventService.PublishEventAsync(id, adminId.Value);
        return NoContent();
    }

    [HttpPost("admin/{id}/reject")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> RejectEvent(int id, [FromBody] AdminEventActionDto dto)
    {
        var adminId = GetUserId();
        if (adminId == null) return Unauthorized();
        await _eventService.RejectEventAsync(id, adminId.Value, dto.Note);
        return NoContent();
    }

    [HttpPost("admin/{id}/archive")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ArchiveEvent(int id)
    {
        var adminId = GetUserId();
        if (adminId == null) return Unauthorized();
        await _eventService.ArchiveEventAsync(id, adminId.Value);
        return NoContent();
    }

    [HttpPost("admin/{id}/feature")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> FeatureEvent(int id)
    {
        var adminId = GetUserId();
        if (adminId == null) return Unauthorized();
        await _eventService.FeatureEventAsync(id, adminId.Value, true);
        return NoContent();
    }

    [HttpDelete("admin/{id}/feature")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UnfeatureEvent(int id)
    {
        var adminId = GetUserId();
        if (adminId == null) return Unauthorized();
        await _eventService.FeatureEventAsync(id, adminId.Value, false);
        return NoContent();
    }

    [HttpDelete("admin/{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteEvent(int id)
    {
        var adminId = GetUserId();
        if (adminId == null) return Unauthorized();
        await _eventService.DeleteEventAsync(id, adminId.Value);
        return NoContent();
    }

    [HttpPut("admin/{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateEvent(int id, [FromBody] CreateEventDto dto)
    {
        var adminId = GetUserId();
        if (adminId == null) return Unauthorized();
        var result = await _eventService.UpdateEventAsync(id, dto, adminId.Value);
        return Ok(result);
    }
}

public class AdminEventActionDto
{
    public string? Note { get; set; }
}
