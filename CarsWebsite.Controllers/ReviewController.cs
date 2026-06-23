using cars_website_api.CarsWebsite.DTOs.Review;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace cars_website_api.CarsWebsite.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("global")]
public class ReviewController : ControllerBase
{
    private readonly IReviewService _reviewService;

    public ReviewController(IReviewService reviewService)
    {
        _reviewService = reviewService;
    }

    private int GetUserId()
    {
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid);
        return uid;
    }

    private bool IsAdmin()
        => User.HasClaim("isAdmin", "true");

    [HttpGet]
    public async Task<IActionResult> GetSellerReviews(
        [FromQuery] int sellerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
        => Ok(await _reviewService.GetSellerReviewsAsync(sellerId, page, pageSize));

    [Authorize]
    [HttpGet("received")]
    public async Task<IActionResult> GetMyReceivedReviews(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        return Ok(await _reviewService.GetMyReceivedReviewsAsync(userId, page, pageSize));
    }

    [Authorize]
    [HttpGet("given")]
    public async Task<IActionResult> GetMyGivenReviews(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        return Ok(await _reviewService.GetMyGivenReviewsAsync(userId, page, pageSize));
    }

    [Authorize]
    [HttpGet("can-review")]
    public async Task<IActionResult> CanReview([FromQuery] int sellerId)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        var canReview = await _reviewService.CanReviewAsync(userId, sellerId);
        return Ok(new { canReview });
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreateReview([FromBody] CreateReviewDto dto)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            var review = await _reviewService.CreateReviewAsync(userId, dto);
            return Ok(review);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [Authorize]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteReview(int id)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            await _reviewService.DeleteReviewAsync(id, userId, IsAdmin());
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }
}
