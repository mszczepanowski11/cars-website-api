using cars_website_api.CarsWebsite.DTOs.Financing;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace cars_website_api.CarsWebsite.Controllers;

[ApiController]
[Route("api/FinancingInquiry")]
[EnableRateLimiting("global")]
public class FinancingController : ControllerBase
{
    private readonly IFinancingService _financing;
    private readonly ILogger<FinancingController> _logger;

    public FinancingController(IFinancingService financing, ILogger<FinancingController> logger)
    {
        _financing = financing;
        _logger = logger;
    }

    private int? TryGetUserId()
    {
        if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) && uid > 0)
            return uid;
        return null;
    }

    /// <summary>
    /// Submit a financing inquiry (leasing or credit) for an advert.
    /// Authentication is optional — guest submissions are allowed.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> Create([FromBody] CreateFinancingInquiryDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { message = "Nieprawidłowe dane formularza.", errors = ModelState });

        try
        {
            var id = await _financing.CreateInquiryAsync(dto, TryGetUserId());

            _logger.LogInformation("[Financing] POST /api/FinancingInquiry → lead #{Id} (advert={AdvertId})", id, dto.AdvertId);

            return Ok(new
            {
                id,
                message = "Zapytanie zostało przyjęte. Skontaktujemy się z Tobą wkrótce."
            });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning("[Financing] Advert not found: {Msg}", ex.Message);
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Financing] Unhandled error for advert {AdvertId}", dto.AdvertId);
            return StatusCode(500, new { message = "Błąd serwera. Spróbuj ponownie za chwilę." });
        }
    }
}
