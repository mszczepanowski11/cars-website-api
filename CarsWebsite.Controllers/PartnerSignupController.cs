using cars_website_api.CarsWebsite.DTOs.Partner;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace cars_website_api.CarsWebsite.Controllers;

// Public "Dla firm" self-service signup - no [Authorize], anyone can submit a company + feed URL.
// Nothing here creates a live account/Partner on its own; submissions queue as Pending for admin
// review (see AdminPartnerController's signup-requests endpoints).
[ApiController]
[Route("api/partner-signup")]
[EnableRateLimiting("strict")]
public class PartnerSignupController : ControllerBase
{
    private readonly IPartnerSignupService _signupService;

    public PartnerSignupController(IPartnerSignupService signupService)
    {
        _signupService = signupService;
    }

    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] PartnerSignupInputDto dto)
    {
        try
        {
            return Ok(await _signupService.PreviewAsync(dto));
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] PartnerSignupInputDto dto)
    {
        try
        {
            return Ok(await _signupService.SubmitAsync(dto));
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
