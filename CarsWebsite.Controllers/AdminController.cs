using cars_website_api.CarsWebsite.DTOs.Admin;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;
    private readonly AppDbContext _context;

    public AdminController(IAdminService adminService, AppDbContext context)
    {
        _adminService = adminService;
        _context = context;
    }

    private async Task<bool> IsAdminAsync()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId)) return false;
        var user = await _context.Users.FindAsync(userId);
        return user?.IsAdmin == true;
    }

    private int GetUserId()
    {
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
        return userId;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        if (!await IsAdminAsync()) return Forbid();
        return Ok(await _adminService.GetStatsAsync());
    }

    [HttpGet("reports")]
    public async Task<IActionResult> GetReports([FromQuery] AdminReportFilterDto filter)
    {
        if (!await IsAdminAsync()) return Forbid();
        return Ok(await _adminService.GetReportsAsync(filter));
    }

    [HttpGet("reports/{id}")]
    public async Task<IActionResult> GetReport(int id)
    {
        if (!await IsAdminAsync()) return Forbid();
        var report = await _adminService.GetReportByIdAsync(id);
        if (report == null) return NotFound();
        return Ok(report);
    }

    [HttpPost("reports/{id}/resolve")]
    public async Task<IActionResult> ResolveReport(int id, [FromBody] AdminResolveReportDto dto)
    {
        if (!await IsAdminAsync()) return Forbid();
        await _adminService.ResolveReportAsync(id, GetUserId(), dto.Note);
        return NoContent();
    }

    [HttpPost("reports/{id}/reject")]
    public async Task<IActionResult> RejectReport(int id, [FromBody] AdminResolveReportDto dto)
    {
        if (!await IsAdminAsync()) return Forbid();
        await _adminService.RejectReportAsync(id, GetUserId(), dto.Note);
        return NoContent();
    }

    [HttpPost("adverts/{id}/hide")]
    public async Task<IActionResult> HideAdvert(int id, [FromBody] AdminActionRequestDto dto)
    {
        if (!await IsAdminAsync()) return Forbid();
        await _adminService.HideAdvertAsync(id, GetUserId(), dto.Note);
        return NoContent();
    }

    [HttpPost("adverts/{id}/unhide")]
    public async Task<IActionResult> UnhideAdvert(int id)
    {
        if (!await IsAdminAsync()) return Forbid();
        await _adminService.UnhideAdvertAsync(id, GetUserId());
        return NoContent();
    }

    [HttpDelete("adverts/{id}")]
    public async Task<IActionResult> DeleteAdvert(int id, [FromBody] AdminActionRequestDto dto)
    {
        if (!await IsAdminAsync()) return Forbid();
        await _adminService.DeleteAdvertAsync(id, GetUserId(), dto.Note);
        return NoContent();
    }

    [HttpPost("adverts/{id}/activate")]
    public async Task<IActionResult> ActivateAdvert(int id)
    {
        if (!await IsAdminAsync()) return Forbid();
        await _adminService.ActivateAdvertAsync(id, GetUserId());
        return NoContent();
    }

    [HttpPost("adverts/{id}/deactivate")]
    public async Task<IActionResult> DeactivateAdvert(int id)
    {
        if (!await IsAdminAsync()) return Forbid();
        await _adminService.DeactivateAdvertAsync(id, GetUserId());
        return NoContent();
    }

    [HttpPost("users/{id}/block")]
    public async Task<IActionResult> BlockUser(int id, [FromBody] AdminBlockUserDto dto)
    {
        if (!await IsAdminAsync()) return Forbid();
        await _adminService.BlockUserAsync(id, GetUserId(), dto.Reason);
        return NoContent();
    }

    [HttpPost("users/{id}/unblock")]
    public async Task<IActionResult> UnblockUser(int id)
    {
        if (!await IsAdminAsync()) return Forbid();
        await _adminService.UnblockUserAsync(id, GetUserId());
        return NoContent();
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (!await IsAdminAsync()) return Forbid();
        return Ok(await _adminService.GetUsersAsync(search, page, pageSize));
    }

    [HttpGet("adverts")]
    public async Task<IActionResult> GetAdverts(
        [FromQuery] string? search,
        [FromQuery] bool? isHidden,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!await IsAdminAsync()) return Forbid();
        return Ok(await _adminService.GetAdvertsAsync(search, isHidden, isActive, page, pageSize));
    }

    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (!await IsAdminAsync()) return Forbid();
        return Ok(await _adminService.GetActionLogsAsync(page, pageSize));
    }
}