namespace cars_website_api.CarsWebsite.DTOs.Admin
{
    public class AdminActionRequestDto
    {
        public string? Note { get; set; }
    }

    public class AdminBlockUserDto
    {
        public string? Reason { get; set; }
    }

    public class AdminResolveReportDto
    {
        public string? Note { get; set; }
    }

    public class AdminReviewCustomCategoryDto
    {
        public string? Notes { get; set; }
    }
}