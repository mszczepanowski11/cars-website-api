using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs.Admin
{
    public class AdminActionRequestDto
    {
        [MaxLength(1000)]
        public string? Note { get; set; }
    }

    public class AdminBlockUserDto
    {
        [MaxLength(1000)]
        public string? Reason { get; set; }
    }

    public class AdminResolveReportDto
    {
        [MaxLength(1000)]
        public string? Note { get; set; }
    }

    public class AdminReviewCustomCategoryDto
    {
        [MaxLength(1000)]
        public string? Notes { get; set; }
    }
}