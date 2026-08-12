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

    // Manual trigger for a partner's FeedUrl - previously the only way to run a sync was to wait
    // up to 6h for the next PartnerFeedSyncJob cycle, with no way to check "is this partner's feed
    // actually working" without waiting for it to fail silently in the background first.
    [HttpPost("{id}/sync-now")]
    public async Task<IActionResult> SyncNow(int id)
    {
        try
        {
            return Ok(await _partnerService.SyncNowAsync(id));
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // Schema adapter layer (CTO audit Etap 2): lets an admin configure a partner's own field
    // names and taxonomy value strings without writing new C# parsing code per integrator. Each
    // PUT replaces the partner's full mapping set (matches the edit-form save pattern used
    // elsewhere in this controller), not a granular per-row CRUD.
    [HttpGet("{id}/field-mappings")]
    public async Task<IActionResult> GetFieldMappings(int id)
        => Ok(await _partnerService.GetFieldMappingsAsync(id));

    [HttpPut("{id}/field-mappings")]
    public async Task<IActionResult> SetFieldMappings(int id, [FromBody] List<PartnerFieldMappingDto> mappings)
    {
        try { return Ok(await _partnerService.SetFieldMappingsAsync(id, mappings)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("{id}/value-mappings")]
    public async Task<IActionResult> GetValueMappings(int id)
        => Ok(await _partnerService.GetValueMappingsAsync(id));

    [HttpPut("{id}/value-mappings")]
    public async Task<IActionResult> SetValueMappings(int id, [FromBody] List<PartnerValueMappingDto> mappings)
    {
        try { return Ok(await _partnerService.SetValueMappingsAsync(id, mappings)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

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
