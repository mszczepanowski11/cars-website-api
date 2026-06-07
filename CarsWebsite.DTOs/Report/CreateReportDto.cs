using System.ComponentModel.DataAnnotations;
using CarsWebsite;

namespace cars_website_api.CarsWebsite.DTOs.Report
{
    public class CreateReportDto
    {
        [Required]
        public ReportTargetType TargetType { get; set; }
        public int? TargetAdvertId { get; set; }
        public int? TargetUserId { get; set; }
        [Required]
        public ReportReason Reason { get; set; }
        [MaxLength(2000)]
        public string? Content { get; set; }
    }
}