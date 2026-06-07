namespace cars_website_api.CarsWebsite.DTOs.Admin
{
    public class AdminActionLogDto
    {
        public int Id { get; set; }
        public int AdminUserId { get; set; }
        public string AdminName { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public int? TargetAdvertId { get; set; }
        public int? TargetUserId { get; set; }
        public int? ReportId { get; set; }
        public string? Note { get; set; }
        public DateTime PerformedAt { get; set; }
    }
}