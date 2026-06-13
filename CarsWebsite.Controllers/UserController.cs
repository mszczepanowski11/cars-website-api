using cars_website_api.CarsWebsite.DTOs.User;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace cars_website_api.CarsWebsite.Controllers;

[Route("api/[controller]")]
[ApiController]
public class UserController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IFollowService _followService;

    public UserController(IUserService userService, IFollowService followService)
    {
        _userService = userService;
        _followService = followService;
    }

    private int GetUserId()
    {
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid);
        return uid;
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetUser()
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        var user = await _userService.GetById(userId);
        if (user == null) return NotFound();

        return Ok(new
        {
            user.Id, user.Name, user.Surname, user.Email, user.PhoneNumber,
            user.AccountType, user.CompanyName, user.Nip,
            user.IsAdmin, user.IsBlocked, user.AvatarUrl, user.EmailVerified, user.LastLoginAt,
            CreatedAt = user.CreatedAt,
            user.City, user.Region, user.About, user.Street, user.PostalCode, user.Country
        });
    }

    [Authorize]
    [HttpPatch("me")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        try
        {
            var user = await _userService.UpdateProfileAsync(userId, dto);
            return Ok(new
            {
                user.Id, user.Name, user.Surname, user.Email, user.PhoneNumber,
                user.AccountType, user.CompanyName, user.Nip,
                user.IsAdmin, user.AvatarUrl, user.EmailVerified,
                CreatedAt = user.CreatedAt,
                user.City, user.Region, user.About, user.Street, user.PostalCode, user.Country
            });
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [Authorize]
    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        try
        {
            await _userService.UpdatePasswordAsync(userId, dto.CurrentPassword, dto.NewPassword);
            return NoContent();
        }
        catch (UnauthorizedAccessException) { return BadRequest(new { message = "Aktualne hasło jest nieprawidłowe." }); }
    }

    [Authorize]
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        return Ok(await _userService.GetSettingsAsync(userId));
    }

    [Authorize]
    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UserSettingsDto dto)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        return Ok(await _userService.UpdateSettingsAsync(userId, dto));
    }

    [Authorize]
    [HttpDelete("me")]
    public async Task<IActionResult> DeleteAccount()
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        await _userService.DeleteAccountAsync(userId);
        return NoContent();
    }

    [Authorize]
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        return Ok(await _userService.GetUserStatsAsync(userId));
    }

    [HttpGet("{id:int}/public")]
    public async Task<IActionResult> GetPublicProfile(int id)
    {
        var profile = await _userService.GetPublicProfileAsync(id);
        if (profile == null) return NotFound();
        return Ok(profile);
    }

    [HttpGet("{id:int}/stats")]
    public async Task<IActionResult> GetPublicStats(int id)
        => Ok(await _userService.GetPublicStatsAsync(id));

    [HttpGet("{id:int}/reviews")]
    public async Task<IActionResult> GetUserReviews(
        int id,
        [FromServices] IReviewService reviewService,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
        => Ok(await reviewService.GetSellerReviewsAsync(id, page, pageSize));

    [Authorize]
    [HttpPost("{id:int}/follow")]
    public async Task<IActionResult> Follow(int id)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        await _followService.FollowAsync(userId, id);
        return NoContent();
    }

    [Authorize]
    [HttpDelete("{id:int}/follow")]
    public async Task<IActionResult> Unfollow(int id)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        await _followService.UnfollowAsync(userId, id);
        return NoContent();
    }

    [Authorize]
    [HttpGet("{id:int}/is-following")]
    public async Task<IActionResult> IsFollowing(int id)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        return Ok(new { isFollowing = await _followService.IsFollowingAsync(userId, id) });
    }
}
