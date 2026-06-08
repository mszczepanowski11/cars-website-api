using CarsWebsite;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
public class StatsController : ControllerBase
{
    private readonly AppDbContext _context;
    public StatsController(AppDbContext context) => _context = context;

    [HttpGet("home")]
    public async Task<IActionResult> GetHomeStats()
    {
        var activeAdverts = await _context.Adverts.CountAsync(a => a.IsActive && !a.IsHidden);
        var totalUsers = await _context.Users.CountAsync();
        var soldVehicles = await _context.Adverts.CountAsync(a => !a.IsActive);
        var companies = await _context.Users.CountAsync(u => u.AccountType == AccountType.Business);
        var events = await _context.Events.CountAsync(e => e.Status == EventStatus.Published);
        return Ok(new { activeAdverts, totalUsers, soldVehicles, companies, events });
    }
}
