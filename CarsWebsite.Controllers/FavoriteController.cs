using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FavoriteController : ControllerBase
{
    private readonly IFavoriteService _favoriteService;

    public FavoriteController(IFavoriteService favoriteService) => _favoriteService = favoriteService;

    private int GetUserId()
    {
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid);
        return uid;
    }

    [HttpGet]
    public async Task<IActionResult> GetFavorites([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var uid = GetUserId(); if (uid == 0) return Unauthorized();
        return Ok(await _favoriteService.GetUserFavoritesAsync(uid, page, pageSize));
    }

    [HttpPost("{advertId}")]
    public async Task<IActionResult> Add(int advertId)
    {
        var uid = GetUserId(); if (uid == 0) return Unauthorized();
        await _favoriteService.AddFavoriteAsync(uid, advertId);
        return Ok();
    }

    [HttpDelete("{advertId}")]
    public async Task<IActionResult> Remove(int advertId)
    {
        var uid = GetUserId(); if (uid == 0) return Unauthorized();
        await _favoriteService.RemoveFavoriteAsync(uid, advertId);
        return NoContent();
    }

    [HttpGet("{advertId}/check")]
    public async Task<IActionResult> Check(int advertId)
    {
        var uid = GetUserId(); if (uid == 0) return Unauthorized();
        return Ok(new { isFavorite = await _favoriteService.IsFavoriteAsync(uid, advertId) });
    }
}