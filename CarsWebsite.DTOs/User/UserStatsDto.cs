namespace cars_website_api.CarsWebsite.DTOs;

public class UserStatsDto
{
    public int TotalAdverts { get; set; }
    public int ActiveAdverts { get; set; }
    public int TotalViews { get; set; }
    public int FavoritesCount { get; set; }
    public int UnreadMessages { get; set; }
    public int TotalSold { get; set; }
    public int FollowersCount { get; set; }
    public int FollowingCount { get; set; }
    public double AverageRating { get; set; }
    public int ReviewCount { get; set; }
    public double ResponseRate { get; set; }
    public double AvgResponseMinutes { get; set; }
}
