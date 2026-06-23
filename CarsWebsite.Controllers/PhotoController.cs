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
    public async Task<IActionResult> AnalyzePhoto([FromBody] AnalyzePhotoRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ImageUrl))
            return BadRequest("imageUrl is required");

        var result = await _photoAnalysisService.AnalyzeAsync(request.ImageUrl);
        return Ok(result);
    }
}

public record AnalyzePhotoRequest(string ImageUrl);
