using cars_website_api.CarsWebsite.DTOs.Partner;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace cars_website_api.CarsWebsite.Controllers;

// Admin CRUD for Partner API integrations - creating/deactivating partners, regenerating API
// keys, and reviewing import history. The actual feed submission endpoint lives on
// PartnerImportController, authenticated separately via X-Api-Key rather than the admin JWT.
[ApiController]
[Route("api/admin/partners")]
[Authorize(Policy = "AdminOnly")]
[EnableRateLimiting("global")]
public class AdminPartnerController : CarizoControllerBase
{
    private readonly IPartnerService _partnerService;
    private readonly IPartnerSignupService _signupService;

    public AdminPartnerController(IPartnerService partnerService, IPartnerSignupService signupService)
    {
        _partnerService = partnerService;
        _signupService = signupService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
        => Ok(await _partnerService.GetAllAsync());

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
        => Ok(await _partnerService.GetByIdAsync(id));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePartnerDto dto)
    {
        var (partner, apiKey) = await _partnerService.CreateAsync(dto);
        return Ok(new PartnerApiKeyResponseDto { PartnerId = partner.Id, ApiKey = apiKey });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePartnerDto dto)
        => Ok(await _partnerService.UpdateAsync(id, dto));

    [HttpPost("{id}/regenerate-key")]
    public async Task<IActionResult> RegenerateApiKey(int id)
    {
        var apiKey = await _partnerService.RegenerateApiKeyAsync(id);
        return Ok(new PartnerApiKeyResponseDto { PartnerId = id, ApiKey = apiKey });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _partnerService.DeleteAsync(id);
        return NoContent();
    }

    [HttpGet("{id}/import-logs")]
    public async Task<IActionResult> GetImportLogs(int id, [FromQuery] int limit = 20)
        => Ok(await _partnerService.GetImportLogsAsync(id, limit));

    // Reviews for the public "Dla firm" self-service signup form (PartnerSignupController) -
    // separate from the CRUD above, which an admin uses to create partners directly.
    [HttpGet("signup-requests")]
    public async Task<IActionResult> GetSignupRequests([FromQuery] string? status)
        => Ok(await _signupService.GetAllAsync(status));

    [HttpPost("signup-requests/{id}/approve")]
    public async Task<IActionResult> ApproveSignupRequest(int id)
    {
        try
        {
            return Ok(await _signupService.ApproveAsync(id, GetUserId()));
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("signup-requests/{id}/reject")]
    public async Task<IActionResult> RejectSignupRequest(int id, [FromBody] RejectPartnerSignupDto dto)
    {
        try
        {
            await _signupService.RejectAsync(id, GetUserId(), dto.Reason);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
