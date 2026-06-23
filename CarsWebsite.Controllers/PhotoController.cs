using cars_website_api.CarsWebsite.Services;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/photos")]
public class PhotoController : ControllerBase
{
    private readonly IPhotoAnalysisService _photoAnalysisService;

    public PhotoController(IPhotoAnalysisService photoAnalysisService)
    {
        _photoAnalysisService = photoAnalysisService;
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> AnalyzePhoto([FromBody] AnalyzePhotoRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ImageUrl))
            return BadRequest("imageUrl is required");

        var result = await _photoAnalysisService.AnalyzeAsync(request.ImageUrl);
        return Ok(result);
    }
}

public record AnalyzePhotoRequest(string ImageUrl);
