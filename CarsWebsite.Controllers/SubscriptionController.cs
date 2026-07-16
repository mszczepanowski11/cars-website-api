using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("global")]
public class SubscriptionController : CarizoControllerBase
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<SubscriptionController> _logger;

    public SubscriptionController(ISubscriptionService subscriptionService, ILogger<SubscriptionController> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    [HttpGet("plans")]
    public IActionResult GetPlans() => Ok(_subscriptionService.GetPlans());

    [Authorize]
    [HttpGet("my")]
    public async Task<IActionResult> GetMySubscription()
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            return Ok(await _subscriptionService.GetMySubscriptionAsync(userId));
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [Authorize]
    [HttpPost("activate-start-program")]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> ActivateStartProgram()
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        try
        {
            await _subscriptionService.ActivateStartProgramAsync(userId);
            _logger.LogInformation("[Subscription] StartProgram activated by userId={UserId}", userId);
            return Ok(new { message = "Program Start został aktywowany. Masz 3 miesiące bezpłatnej subskrypcji." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
