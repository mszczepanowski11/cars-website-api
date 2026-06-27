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
        var activeAdvertsTask = _context.CarAdverts.AsNoTracking().CountAsync(a => a.IsActive && !a.IsHidden);
        var totalUsersTask    = _context.Users.AsNoTracking().CountAsync();
        var soldVehiclesTask  = _context.CarAdverts.AsNoTracking().CountAsync(a => a.SoldAt != null);
        var companiesTask     = _context.Users.AsNoTracking().CountAsync(u => u.AccountType == AccountType.Business);
        var eventsTask        = _context.Events.AsNoTracking().CountAsync(e => e.Status == EventStatus.Published);

        await Task.WhenAll(activeAdvertsTask, totalUsersTask, soldVehiclesTask, companiesTask, eventsTask);

        return new HomeStatsDto(activeAdvertsTask.Result, totalUsersTask.Result, soldVehiclesTask.Result, companiesTask.Result, eventsTask.Result);
    }
}
