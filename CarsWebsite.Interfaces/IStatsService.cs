namespace cars_website_api.CarsWebsite.Interfaces;

public interface IStatsService
{
    Task<HomeStatsDto> GetHomeStatsAsync();
}

public record HomeStatsDto(int ActiveAdverts, int TotalUsers, int SoldVehicles, int Companies, int Events);
