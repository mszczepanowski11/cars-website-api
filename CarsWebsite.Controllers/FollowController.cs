using CarsWebsite;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace cars_website_api.CarsWebsite.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("global")]
public class FollowController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IFollowService _followService;

    public FollowController(AppDbContext context, IFollowService followService)
    {
        _context = context;
        _followService = followService;
    }

    private int GetUserId()
    {
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid);
        return uid;
    }

    // ── Advert follows (maps to FavoriteAdverts) ─────────────────────────────

    [HttpGet("adverts")]
    public async Task<IActionResult> GetFollowedAdverts([FromQuery] int page = 1, [FromQuery] int pageSize = 12)
    {
        var uid = GetUserId(); if (uid == 0) return Unauthorized();
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.FavoriteAdverts
            .Where(f => f.UserId == uid)
            .Include(f => f.Advert)
                .ThenInclude(a => a.Brand)
            .Include(f => f.Advert)
                .ThenInclude(a => a.Model)
            .Include(f => f.Advert)
                .ThenInclude(a => a.Images)
            .OrderByDescending(f => f.CreatedAt);

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var result = items.Select(f => new
        {
            id = f.AdvertId,
            advertId = f.AdvertId,
            advertTitle = f.Advert?.Title ?? "",
            advertPrice = f.Advert?.Price ?? 0,
            priceAtFollow = f.Advert?.Price ?? 0,
            priceChanged = false,
            city = f.Advert?.City,
            brand = f.Advert?.Brand?.Name,
            model = f.Advert?.Model?.Name,
            mainImageUrl = f.Advert?.Images?.FirstOrDefault(i => i.IsMain)?.Url ?? f.Advert?.Images?.OrderBy(i => i.Order).FirstOrDefault()?.Url,
            createdAt = f.CreatedAt.ToString("o"),
        }).ToList();

        return Ok(new { items = result, totalCount = total });
    }

    [HttpGet("advert/{advertId}/status")]
    public async Task<IActionResult> AdvertStatus(int advertId)
    {
        var uid = GetUserId(); if (uid == 0) return Unauthorized();
        var isFollowing = await _context.FavoriteAdverts
            .AnyAsync(f => f.UserId == uid && f.AdvertId == advertId);
        return Ok(new { isFollowing });
    }

    [HttpPost("advert/{advertId}")]
    public async Task<IActionResult> FollowAdvert(int advertId)
    {
        var uid = GetUserId(); if (uid == 0) return Unauthorized();
        var exists = await _context.FavoriteAdverts.AnyAsync(f => f.UserId == uid && f.AdvertId == advertId);
        if (!exists)
        {
            _context.FavoriteAdverts.Add(new FavoriteAdvert { UserId = uid, AdvertId = advertId });
            await _context.SaveChangesAsync();
        }
        return Ok();
    }

    [HttpDelete("advert/{advertId}")]
    public async Task<IActionResult> UnfollowAdvert(int advertId)
    {
        var uid = GetUserId(); if (uid == 0) return Unauthorized();
        var fav = await _context.FavoriteAdverts.FirstOrDefaultAsync(f => f.UserId == uid && f.AdvertId == advertId);
        if (fav != null)
        {
            _context.FavoriteAdverts.Remove(fav);
            await _context.SaveChangesAsync();
        }
        return NoContent();
    }

    // ── Seller follows (maps to UserFollows) ──────────────────────────────────

    [HttpGet("sellers")]
    public async Task<IActionResult> GetFollowedSellers([FromQuery] int page = 1, [FromQuery] int pageSize = 12)
    {
        var uid = GetUserId(); if (uid == 0) return Unauthorized();
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.UserFollows
            .Where(f => f.FollowerId == uid)
            .Include(f => f.Followed)
            .OrderByDescending(f => f.FollowedAt);

        var total = await query.CountAsync();
        var follows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var sellerIds = follows.Select(f => f.FollowedId).ToList();
        var advertCounts = await _context.CarAdverts
            .Where(a => sellerIds.Contains(a.UserId) && a.IsActive)
            .GroupBy(a => a.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);

        var result = follows.Select(f => new
        {
            id = f.Id,
            sellerId = f.FollowedId,
            sellerName = f.Followed?.Name ?? f.Followed?.Email ?? $"Użytkownik #{f.FollowedId}",
            advertCount = advertCounts.TryGetValue(f.FollowedId, out var cnt) ? cnt : 0,
            createdAt = f.FollowedAt.ToString("o"),
            averageRating = (double?)null,
        }).ToList();

        return Ok(new { items = result, totalCount = total });
    }

    [HttpGet("followers")]
    public async Task<IActionResult> GetFollowers([FromQuery] int page = 1, [FromQuery] int pageSize = 12)
    {
        var uid = GetUserId(); if (uid == 0) return Unauthorized();
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.UserFollows
            .Where(f => f.FollowedId == uid)
            .Include(f => f.Follower)
            .OrderByDescending(f => f.FollowedAt);

        var total = await query.CountAsync();
        var follows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var result = follows.Select(f => new
        {
            id = f.Id,
            followerId = f.FollowerId,
            followerName = f.Follower?.Name ?? f.Follower?.Email ?? $"Użytkownik #{f.FollowerId}",
            createdAt = f.FollowedAt.ToString("o"),
        }).ToList();

        return Ok(new { items = result, totalCount = total });
    }

    [HttpGet("seller/{sellerId}/status")]
    public async Task<IActionResult> SellerStatus(int sellerId)
    {
        var uid = GetUserId(); if (uid == 0) return Unauthorized();
        var isFollowing = await _followService.IsFollowingAsync(uid, sellerId);
        return Ok(new { isFollowing });
    }

    [HttpPost("seller/{sellerId}")]
    public async Task<IActionResult> FollowSeller(int sellerId)
    {
        var uid = GetUserId(); if (uid == 0) return Unauthorized();
        if (uid == sellerId) return BadRequest(new { message = "Nie możesz obserwować siebie." });
        await _followService.FollowAsync(uid, sellerId);
        return Ok();
    }

    [HttpDelete("seller/{sellerId}")]
    public async Task<IActionResult> UnfollowSeller(int sellerId)
    {
        var uid = GetUserId(); if (uid == 0) return Unauthorized();
        await _followService.UnfollowAsync(uid, sellerId);
        return NoContent();
    }
}
