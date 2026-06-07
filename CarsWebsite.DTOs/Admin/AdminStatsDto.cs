namespace cars_website_api.CarsWebsite.DTOs.Admin
{
    public class AdminStatsDto
    {
        public int TotalActiveAdverts { get; set; }
        public int TotalUsers { get; set; }
        public int TotalReports { get; set; }
        public int PendingReports { get; set; }
        public int NewRegistrationsThisMonth { get; set; }
        public int BlockedUsers { get; set; }
    }
}