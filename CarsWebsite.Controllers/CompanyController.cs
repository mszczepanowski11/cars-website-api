using cars_website_api.CarsWebsite.DTOs.Company;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace cars_website_api.CarsWebsite.Controllers;

// Owner+Member multi-user company accounts (CTO audit Etap 3). "Owner" is not a separate role
// here - it is simply the Business-type account itself; this controller only manages the Member
// side (invite/list/remove/accept/leave). Advert-editing authorization for Members lives in
// AdvertController via ICompanyService.GetEffectiveOwnerIdAsync, not here.
[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("global")]
[Authorize]
public class CompanyController : CarizoControllerBase
{
    private readonly ICompanyService _companyService;

    public CompanyController(ICompanyService companyService)
    {
        _companyService = companyService;
    }

    [HttpGet("context")]
    public async Task<IActionResult> GetMyContext()
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        return Ok(await _companyService.GetMyContextAsync(userId));
    }

    [HttpGet("members")]
    public async Task<IActionResult> GetMembers()
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        return Ok(await _companyService.GetMembersAsync(userId));
    }

    [HttpPost("members/invite")]
    public async Task<IActionResult> InviteMember([FromBody] InviteMemberDto dto)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            await _companyService.InviteMemberAsync(userId, dto.Email);
            return Ok(new { message = "Zaproszenie zostało wysłane." });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpDelete("members/{membershipId}/invite")]
    public async Task<IActionResult> CancelInvite(int membershipId)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            await _companyService.CancelInviteAsync(userId, membershipId);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpDelete("members/{membershipId}")]
    public async Task<IActionResult> RemoveMember(int membershipId)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            await _companyService.RemoveMemberAsync(userId, membershipId);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost("invites/accept")]
    public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteDto dto)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            await _companyService.AcceptInviteAsync(dto.Token, userId);
            return Ok(new { message = "Dołączono do zespołu." });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost("leave")]
    public async Task<IActionResult> Leave()
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            await _companyService.LeaveCompanyAsync(userId);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
