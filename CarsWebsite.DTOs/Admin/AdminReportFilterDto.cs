namespace cars_website_api.CarsWebsite.DTOs.Admin
{
    public class AdminReportFilterDto
    {
        public string? Status { get; set; }
        public string? TargetType { get; set; }
        public string? Reason { get; set; }
        public string? Search { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}