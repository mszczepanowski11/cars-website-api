using cars_website_api.CarsWebsite.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

[ApiController]
[Route("api/photos")]
[EnableRateLimiting("global")]
public class PhotoController : ControllerBase
{
    private readonly IPhotoAnalysisService _photoAnalysisService;

    public PhotoController(IPhotoAnalysisService photoAnalysisService)
    {
        _photoAnalysisService = photoAnalysisService;
    }

    [HttpPost("analyze")]
    [Authorize]
    [EnableRateLimiting("ai")]
    public async Task<IActionResult> AnalyzePhoto([FromBody] AnalyzePhotoRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ImageUrl))
            return BadRequest("imageUrl is required");

        if (!Uri.TryCreate(request.ImageUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http") ||
            !uri.Host.EndsWith("cloudinary.com", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Nieprawidłowy adres URL zdjęcia.");

        var result = await _photoAnalysisService.AnalyzeAsync(request.ImageUrl);
        return Ok(result);
    }
}

public record AnalyzePhotoRequest(string ImageUrl);
