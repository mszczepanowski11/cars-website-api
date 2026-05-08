namespace cars_website_api.CarsWebsite.DTOs;

public class UserStatsDto
{
    public int TotalAdverts { get; set; }
    public int ActiveAdverts { get; set; }
    public long TotalViews { get; set; }
    public int FavoritesCount { get; set; }
    public int UnreadMessages { get; set; }
}