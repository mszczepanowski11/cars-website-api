using CarsWebsite;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace cars_website_api.CarsWebsite.Controllers;

// Push endpoint for partner XML/CSV feed imports. Deliberately has no [Authorize] - partner
// dealer-management software authenticates via the X-Api-Key header instead of a JWT bearer
// token, since there's no human login involved on this path.
[ApiController]
[Route("api/partner")]
[EnableRateLimiting("strict")]
public class PartnerImportController : ControllerBase
{
    private const long MaxFeedBytes = 10 * 1024 * 1024;

    private readonly IPartnerService _partnerService;
    private readonly IPartnerImportService _importService;

    public PartnerImportController(IPartnerService partnerService, IPartnerImportService importService)
    {
        _partnerService = partnerService;
        _importService = importService;
    }

    [HttpPost("adverts/import")]
    [RequestSizeLimit(MaxFeedBytes)]
    public async Task<IActionResult> Import([FromQuery] string format = "xml")
    {
        if (!Request.Headers.TryGetValue("X-Api-Key", out var apiKeyHeader) || string.IsNullOrWhiteSpace(apiKeyHeader))
            return Unauthorized(new { message = "Brak nagłówka X-Api-Key." });

        var partner = await _partnerService.AuthenticateAsync(apiKeyHeader.ToString());
        if (partner == null)
            return Unauthorized(new { message = "Nieprawidłowy klucz API." });

        if (!Enum.TryParse<PartnerFeedFormat>(format, true, out var feedFormat))
            return BadRequest(new { message = "Nieobsługiwany format. Użyj 'xml' lub 'csv'." });

        string content;
        using (var reader = new StreamReader(Request.Body))
            content = await reader.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(content))
            return BadRequest(new { message = "Puste ciało żądania." });

        var result = await _importService.ImportAsync(partner, content, feedFormat);
        return Ok(result);
    }
}
