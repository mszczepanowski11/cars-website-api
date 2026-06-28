using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs.Admin
{
    public class AdminReportFilterDto
    {
        [MaxLength(50)]
        public string? Status { get; set; }

        [MaxLength(50)]
        public string? TargetType { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }

        [MaxLength(100)]
        public string? Search { get; set; }

        [Range(1, 10_000)]
        public int Page { get; set; } = 1;

        [Range(1, 100)]
        public int PageSize { get; set; } = 20;
    }
}