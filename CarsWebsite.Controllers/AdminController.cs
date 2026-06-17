using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.DTOs.Admin;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;
    private readonly AppDbContext _db;

    public AdminController(IAdminService adminService, AppDbContext db)
    {
        _adminService = adminService;
        _db = db;
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

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(int id, [FromBody] AdminActionRequestDto dto)
    {
        await _adminService.DeleteUserAsync(id, GetUserId(), dto.Note);
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

    // ── Taxonomy management ────────────────────────────────────────────────────

    [HttpGet("features")]
    public async Task<IActionResult> GetFeatures([FromQuery] string? search, [FromQuery] int? categoryId)
    {
        var q = _db.Features.Include(f => f.Category).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(f => f.Name.Contains(search));
        if (categoryId.HasValue)
            q = q.Where(f => f.CategoryId == categoryId.Value);
        var items = await q.OrderBy(f => f.Category.Name).ThenBy(f => f.Name)
            .Select(f => new { f.Id, f.Name, Category = new { f.Category.Id, f.Category.Name, f.Category.VehicleCategoryId } })
            .ToListAsync();
        return Ok(items);
    }

    [HttpPost("features")]
    public async Task<IActionResult> CreateFeature([FromBody] CreateFeatureDto dto)
    {
        var cat = await _db.FeatureCategories.FindAsync(dto.CategoryId);
        if (cat == null) return BadRequest("Kategoria wyposażenia nie istnieje.");
        var feature = new Feature { Name = dto.Name, CategoryId = dto.CategoryId };
        _db.Features.Add(feature);
        await _db.SaveChangesAsync();
        return Ok(new { feature.Id, feature.Name });
    }

    [HttpDelete("features/{id}")]
    public async Task<IActionResult> DeleteFeature(int id)
    {
        var feature = await _db.Features.FindAsync(id);
        if (feature == null) return NotFound();
        _db.Features.Remove(feature);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("feature-categories")]
    public async Task<IActionResult> GetFeatureCategories()
    {
        var items = await _db.FeatureCategories
            .Include(fc => fc.VehicleCategory)
            .OrderBy(fc => fc.Name)
            .Select(fc => new { fc.Id, fc.Name, fc.VehicleCategoryId, VehicleCategoryName = fc.VehicleCategory != null ? fc.VehicleCategory.Name : null })
            .ToListAsync();
        return Ok(items);
    }

    [HttpPost("feature-categories")]
    public async Task<IActionResult> CreateFeatureCategory([FromBody] CreateFeatureCategoryDto dto)
    {
        var cat = new FeatureCategory { Name = dto.Name, VehicleCategoryId = dto.VehicleCategoryId };
        _db.FeatureCategories.Add(cat);
        await _db.SaveChangesAsync();
        return Ok(new { cat.Id, cat.Name });
    }

    [HttpDelete("feature-categories/{id}")]
    public async Task<IActionResult> DeleteFeatureCategory(int id)
    {
        var cat = await _db.FeatureCategories.FindAsync(id);
        if (cat == null) return NotFound();
        _db.FeatureCategories.Remove(cat);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}