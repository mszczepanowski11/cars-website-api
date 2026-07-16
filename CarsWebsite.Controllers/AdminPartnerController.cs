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

    public AdminPartnerController(IPartnerService partnerService)
    {
        _partnerService = partnerService;
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
}
