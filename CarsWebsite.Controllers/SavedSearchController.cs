using cars_website_api.CarsWebsite.DTOs.SavedSearch;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace cars_website_api.CarsWebsite.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("global")]
public class SavedSearchController : CarizoControllerBase
{
    private readonly ISavedSearchService _savedSearchService;

    public SavedSearchController(ISavedSearchService savedSearchService)
    {
        _savedSearchService = savedSearchService;
    }

    [HttpGet]
    public async Task<IActionResult> GetMine([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        return Ok(await _savedSearchService.GetMyAsync(userId, page, pageSize));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSavedSearchDto dto)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            var search = await _savedSearchService.CreateAsync(userId, dto);
            return Ok(search);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateSavedSearchDto dto)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            return Ok(await _savedSearchService.UpdateAsync(id, userId, dto));
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            await _savedSearchService.DeleteAsync(id, userId);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPut("{id:int}/notify")]
    public async Task<IActionResult> SetNotify(int id, [FromBody] SetSavedSearchNotifyDto dto)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            await _savedSearchService.SetNotifyAsync(id, userId, dto.NotifyOnNew);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}
