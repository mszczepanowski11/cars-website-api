using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class StatsController : ControllerBase
{
    private readonly IStatsService _stats;
    public StatsController(IStatsService stats) => _stats = stats;

    [HttpGet("home")]
    public async Task<IActionResult> GetHomeStats()
    {
        var s = await _stats.GetHomeStatsAsync();
        return Ok(new
        {
            activeAdverts = s.ActiveAdverts,
            totalUsers = s.TotalUsers,
            soldVehicles = s.SoldVehicles,
            companies = s.Companies,
            events = s.Events
        });
    }
}
