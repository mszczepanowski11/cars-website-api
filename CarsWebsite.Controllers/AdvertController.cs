using cars_website_api.CarsWebsite.DTOs.Advert;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
public class AdvertController : ControllerBase
{
    private readonly IAdvertService _advertService;
    private readonly IAdvertImageService _imageService;

    public AdvertController(IAdvertService advertService, IAdvertImageService imageService)
    {
        _advertService = advertService;
        _imageService = imageService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _advertService.SearchCarAdvertsAsync(new SearchCarAdvertDto { Page = page, PageSize = pageSize });
        return Ok(result);
    }

    [Authorize]
    [HttpGet("user")]
    public async Task<IActionResult> GetUserAdverts([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var result = await _advertService.GetUserAdvertsAsync(userId, page, pageSize);
        return Ok(result);
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCarAdvertDto dto)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var id = await _advertService.CreateCarAdvertAsync(dto, userId);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    [Authorize]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCarAdvertDto dto)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId))
            return Unauthorized();

        await _advertService.UpdateCarAdvertAsync(id, dto, userId);
        return NoContent();
    }

    [Authorize]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId))
            return Unauthorized();

        await _advertService.DeleteCarAdvertAsync(id, userId);
        return NoContent();
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var advert = await _advertService.GetCarAdvertByIdAsync(id);
        return Ok(advert);
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] SearchCarAdvertDto dto)
    {
        var result = await _advertService.SearchCarAdvertsAsync(dto);
        return Ok(result);
    }

    [Authorize]
    [HttpPost("{advertId}/images")]
    public async Task<IActionResult> UploadImage(int advertId, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("File is empty");
        var url = await _imageService.UploadAdvertImageAsync(advertId, file);
        return Ok(new { url });
    }

    [Authorize]
    [HttpPut("{advertId}/images/{imageId}/set-main")]
    public async Task<IActionResult> SetMainImage(int advertId, int imageId)
    {
        await _imageService.SetMainImageAsync(advertId, imageId);
        return NoContent();
    }

    [Authorize]
    [HttpDelete("{advertId}/images/{imageId}")]
    public async Task<IActionResult> DeleteImage(int advertId, int imageId)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId))
            return Unauthorized();

        await _imageService.DeleteImageAsync(advertId, imageId, userId);
        return NoContent();
    }
}