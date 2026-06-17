using cars_website_api.CarsWebsite.DTOs.Advert;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
public class AdvertController : ControllerBase
{
    private readonly IAdvertService _advertService;
    private readonly IAdvertImageService _imageService;
    private readonly IUserService _userService;

    public AdvertController(IAdvertService advertService, IAdvertImageService imageService, IUserService userService)
    {
        _advertService = advertService;
        _imageService = imageService;
        _userService = userService;
    }

    private int GetUserId()
    {
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid);
        return uid;
    }

    private bool IsAdmin() => User.FindFirstValue("isAdmin") == "true";

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
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        return Ok(await _advertService.GetUserAdvertsAsync(userId, page, pageSize));
    }

    [HttpGet("vin/{vin}")]
    public async Task<IActionResult> GetByVin(string vin)
    {
        var advert = await _advertService.GetByVinAsync(vin);
        if (advert == null) return NotFound();
        return Ok(advert);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var userId = GetUserId();
        try { return Ok(await _advertService.GetCarAdvertByIdAsync(id, userId > 0 ? userId : null, IsAdmin())); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] SearchCarAdvertDto dto)
        => Ok(await _advertService.SearchCarAdvertsAsync(dto));

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCarAdvertDto dto)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        var user = await _userService.GetById(userId);
        if (user != null && user.AccountType == AccountType.Personal)
        {
            var (activeCount, yearCount) = await _advertService.GetPersonalAdCountsAsync(userId);
            if (activeCount >= 1)
                return BadRequest(new { error = "private_limit_active", message = "Wygląda na to, że prowadzisz działalność handlową. Załóż konto biznesowe." });
            if (yearCount >= 3)
                return BadRequest(new { error = "private_limit_yearly", message = "Wygląda na to, że prowadzisz działalność handlową. Załóż konto biznesowe." });
        }

        var id = await _advertService.CreateCarAdvertAsync(dto, userId);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    [Authorize]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCarAdvertDto dto)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            await _advertService.UpdateCarAdvertAsync(id, dto, userId);
            return NoContent();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [Authorize]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            await _advertService.DeleteCarAdvertAsync(id, userId);
            return NoContent();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [Authorize]
    [HttpPost("{id}/promote")]
    public async Task<IActionResult> Promote(int id, [FromBody] PromoteAdvertDto dto)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            await _advertService.PromoteAdvertAsync(id, userId, dto.Type, dto.DurationDays);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [Authorize]
    [HttpPost("{advertId}/images")]
    public async Task<IActionResult> UploadImage(int advertId, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("File is empty");

        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        var url = await _imageService.UploadAdvertImageAsync(advertId, file, userId);
        return Ok(new { url });
    }

    [Authorize]
    [HttpPut("{advertId}/images/{imageId}/set-main")]
    public async Task<IActionResult> SetMainImage(int advertId, int imageId)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        await _imageService.SetMainImageAsync(advertId, imageId, userId);
        return NoContent();
    }

    [Authorize]
    [HttpDelete("{advertId}/images/{imageId}")]
    public async Task<IActionResult> DeleteImage(int advertId, int imageId)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        await _imageService.DeleteImageAsync(advertId, imageId, userId);
        return NoContent();
    }
}
