using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Services;

public class StatsService : IStatsService
{
    private readonly AppDbContext _context;

    public StatsService(AppDbContext context) => _context = context;

    public async Task<HomeStatsDto> GetHomeStatsAsync()
    {
        var activeAdverts = await _context.CarAdverts.CountAsync(a => a.IsActive && !a.IsHidden);
        var totalUsers = await _context.Users.CountAsync();
        var soldVehicles = await _context.CarAdverts.CountAsync(a => a.SoldAt != null);
        var companies = await _context.Users.CountAsync(u => u.AccountType == AccountType.Business);
        var events = await _context.Events.CountAsync(e => e.Status == EventStatus.Published);
        return new HomeStatsDto(activeAdverts, totalUsers, soldVehicles, companies, events);
    }
}
