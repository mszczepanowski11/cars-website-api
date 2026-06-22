using cars_website_api.CarsWebsite.DTOs.Advert;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("global")]
public class AdvertController : ControllerBase
{
    private readonly IAdvertService _advertService;
    private readonly IAdvertImageService _imageService;
    private readonly IUserService _userService;
    private readonly ILogger<AdvertController> _logger;

    public AdvertController(IAdvertService advertService, IAdvertImageService imageService, IUserService userService, ILogger<AdvertController> logger)
    {
        _advertService = advertService;
        _imageService = imageService;
        _userService = userService;
        _logger = logger;
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
            _logger.LogInformation("[Advert/Create] Personal account userId={UserId} activeCount={Active} yearCount={Year}", userId, activeCount, yearCount);
            if (activeCount >= 1)
            {
                _logger.LogWarning("[Advert/Create] Blocked: personal active limit reached userId={UserId} activeCount={Active}", userId, activeCount);
                return BadRequest(new { error = "private_limit_active", message = "Wygląda na to, że prowadzisz działalność handlową. Załóż konto biznesowe." });
            }
            if (yearCount >= 3)
            {
                _logger.LogWarning("[Advert/Create] Blocked: personal yearly limit reached userId={UserId} yearCount={Year}", userId, yearCount);
                return BadRequest(new { error = "private_limit_yearly", message = "Wygląda na to, że prowadzisz działalność handlową. Załóż konto biznesowe." });
            }
        }

        try
        {
            var id = await _advertService.CreateCarAdvertAsync(dto, userId);
            _logger.LogInformation("[Advert/Create] Created advertId={AdvertId} userId={UserId}", id, userId);
            return CreatedAtAction(nameof(GetById), new { id }, new { id });
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("[Advert/Create] Validation failed userId={UserId}: {Msg}", userId, ex.Message);
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[Advert/Create] Business rule rejected userId={UserId}: {Msg}", userId, ex.Message);
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Advert/Create] FAILED userId={UserId} msg={Message} inner={Inner}", userId, ex.Message, ex.InnerException?.Message);
            return StatusCode(500, new { message = ex.InnerException?.Message ?? ex.Message });
        }
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
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
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
    [HttpPost("{id}/sold")]
    public async Task<IActionResult> MarkAsSold(int id)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            await _advertService.MarkAsSoldAsync(id, userId);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [Authorize]
    [HttpPost("{id}/publish")]
    public async Task<IActionResult> Publish(int id)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            await _advertService.PublishAsync(id, userId);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("{id}/promote")]
    public async Task<IActionResult> Promote(int id, [FromBody] PromoteAdvertDto dto)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            var isAdmin = IsAdmin();
            await _advertService.PromoteAdvertAsync(id, userId, dto.Type, dto.DurationDays, isAdmin);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [Authorize]
    [HttpPost("{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            await _advertService.DeactivateAsync(id, userId);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [Authorize]
    [HttpPost("{id:int}/renew")]
    public async Task<IActionResult> Renew(int id)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            await _advertService.RenewAsync(id, userId);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [Authorize]
    [HttpPost("{advertId}/images")]
    public async Task<IActionResult> UploadImage(int advertId, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Plik jest pusty." });

        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        _logger.LogInformation("[ImageUpload] advertId={AdvertId} userId={UserId} file={File} size={Size}B type={Type}",
            advertId, userId, file.FileName, file.Length, file.ContentType);

        try
        {
            var url = await _imageService.UploadAdvertImageAsync(advertId, file, userId);
            _logger.LogInformation("[ImageUpload] OK advertId={AdvertId} url={Url}", advertId, url);
            return Ok(new { url });
        }
        catch (BadHttpRequestException ex)
        {
            _logger.LogWarning("[ImageUpload] Bad request advertId={AdvertId}: {Msg}", advertId, ex.Message);
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning("[ImageUpload] Not found advertId={AdvertId}: {Msg}", advertId, ex.Message);
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("[ImageUpload] Forbidden advertId={AdvertId} userId={UserId}", advertId, userId);
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "[Upload] Cloudinary error advertId={AdvertId}", advertId);
            return StatusCode(502, new { message = "Błąd usługi przechowywania zdjęć. Spróbuj ponownie." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ImageUpload] Unexpected error advertId={AdvertId}", advertId);
            return StatusCode(500, new { message = "Błąd podczas uploadu zdjęcia." });
        }
    }

    [Authorize]
    [HttpPut("{advertId}/images/{imageId}/set-main")]
    public async Task<IActionResult> SetMainImage(int advertId, int imageId)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            await _imageService.SetMainImageAsync(advertId, imageId, userId);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [Authorize]
    [HttpDelete("{advertId}/images/{imageId}")]
    public async Task<IActionResult> DeleteImage(int advertId, int imageId)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            await _imageService.DeleteImageAsync(advertId, imageId, userId);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

}
