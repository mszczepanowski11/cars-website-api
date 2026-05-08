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

    private int GetUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetFavorites([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _favoriteService.GetUserFavoritesAsync(GetUserId(), page, pageSize);
        return Ok(result);
    }

    [HttpPost("{advertId}")]
    public async Task<IActionResult> Add(int advertId)
    {
        await _favoriteService.AddFavoriteAsync(GetUserId(), advertId);
        return Ok();
    }

    [HttpDelete("{advertId}")]
    public async Task<IActionResult> Remove(int advertId)
    {
        await _favoriteService.RemoveFavoriteAsync(GetUserId(), advertId);
        return NoContent();
    }

    [HttpGet("{advertId}/check")]
    public async Task<IActionResult> Check(int advertId)
    {
        var isFav = await _favoriteService.IsFavoriteAsync(GetUserId(), advertId);
        return Ok(new { isFavorite = isFav });
    }
}