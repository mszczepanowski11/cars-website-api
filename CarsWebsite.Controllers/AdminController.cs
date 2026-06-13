using cars_website_api.CarsWebsite.DTOs.Admin;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;

    public AdminController(IAdminService adminService)
    {
        _adminService = adminService;
    }

    private int GetUserId()
    {
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
        return userId;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
        => Ok(await _adminService.GetStatsAsync());

    [HttpGet("reports")]
    public async Task<IActionResult> GetReports([FromQuery] AdminReportFilterDto filter)
        => Ok(await _adminService.GetReportsAsync(filter));

    [HttpGet("reports/{id}")]
    public async Task<IActionResult> GetReport(int id)
    {
        var report = await _adminService.GetReportByIdAsync(id);
        if (report == null) return NotFound();
        return Ok(report);
    }

    [HttpPost("reports/{id}/resolve")]
    public async Task<IActionResult> ResolveReport(int id, [FromBody] AdminResolveReportDto dto)
    {
        await _adminService.ResolveReportAsync(id, GetUserId(), dto.Note);
        return NoContent();
    }

    [HttpPost("reports/{id}/reject")]
    public async Task<IActionResult> RejectReport(int id, [FromBody] AdminResolveReportDto dto)
    {
        await _adminService.RejectReportAsync(id, GetUserId(), dto.Note);
        return NoContent();
    }

    [HttpPost("adverts/{id}/hide")]
    public async Task<IActionResult> HideAdvert(int id, [FromBody] AdminActionRequestDto dto)
    {
        await _adminService.HideAdvertAsync(id, GetUserId(), dto.Note);
        return NoContent();
    }

    [HttpPost("adverts/{id}/unhide")]
    public async Task<IActionResult> UnhideAdvert(int id)
    {
        await _adminService.UnhideAdvertAsync(id, GetUserId());
        return NoContent();
    }

    [HttpDelete("adverts/{id}")]
    public async Task<IActionResult> DeleteAdvert(int id, [FromBody] AdminActionRequestDto dto)
    {
        await _adminService.DeleteAdvertAsync(id, GetUserId(), dto.Note);
        return NoContent();
    }

    [HttpPost("adverts/{id}/activate")]
    public async Task<IActionResult> ActivateAdvert(int id)
    {
        await _adminService.ActivateAdvertAsync(id, GetUserId());
        return NoContent();
    }

    [HttpPost("adverts/{id}/deactivate")]
    public async Task<IActionResult> DeactivateAdvert(int id)
    {
        await _adminService.DeactivateAdvertAsync(id, GetUserId());
        return NoContent();
    }

    [HttpPost("users/{id}/block")]
    public async Task<IActionResult> BlockUser(int id, [FromBody] AdminBlockUserDto dto)
    {
        await _adminService.BlockUserAsync(id, GetUserId(), dto.Reason);
        return NoContent();
    }

    [HttpPost("users/{id}/unblock")]
    public async Task<IActionResult> UnblockUser(int id)
    {
        await _adminService.UnblockUserAsync(id, GetUserId());
        return NoContent();
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _adminService.GetUsersAsync(search, page, pageSize));

    [HttpGet("adverts")]
    public async Task<IActionResult> GetAdverts(
        [FromQuery] string? search,
        [FromQuery] bool? isHidden,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
        => Ok(await _adminService.GetAdvertsAsync(search, isHidden, isActive, page, pageSize));

    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await _adminService.GetActionLogsAsync(page, pageSize));
}