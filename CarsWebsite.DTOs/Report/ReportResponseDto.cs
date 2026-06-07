namespace cars_website_api.CarsWebsite.DTOs.Report
{
    public class ReportResponseDto
    {
        public int Id { get; set; }
        public string TargetType { get; set; } = string.Empty;
        public int? TargetAdvertId { get; set; }
        public string? TargetAdvertTitle { get; set; }
        public int? TargetUserId { get; set; }
        public string? TargetUserName { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string? Content { get; set; }
        public DateTime ReportedAt { get; set; }
        public int ReportedByUserId { get; set; }
        public string ReportedByName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime? ResolvedAt { get; set; }
        public string? AdminNote { get; set; }
    }
}