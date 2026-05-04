using cars_website_api.CarsWebsite.DTOs.Advert;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.AspNetCore.Mvc;

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

    
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCarAdvertDto dto)
    {
        var id = await _advertService.CreateCarAdvertAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCarAdvertDto dto)
    {
        await _advertService.UpdateCarAdvertAsync(id, dto);
        return NoContent();
    }

    
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _advertService.DeleteCarAdvertAsync(id);
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
    
    [HttpPost("{advertId}/images")]
    public async Task<IActionResult> UploadImage(int advertId, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("File is empty");

        var url = await _imageService.UploadAdvertImageAsync(advertId, file);
        return Ok(new { url });
    }

    [HttpPut("{advertId}/images/{imageId}/set-main")]
    public async Task<IActionResult> SetMainImage(int advertId, int imageId)
    {
        await _imageService.SetMainImageAsync(advertId, imageId);
        return NoContent();
    }

    [HttpDelete("{advertId}/images/{imageId}")]
    public async Task<IActionResult> DeleteImage(int advertId, int imageId)
    {
        await _imageService.DeleteImageAsync(advertId, imageId);
        return NoContent();
    }
    
}