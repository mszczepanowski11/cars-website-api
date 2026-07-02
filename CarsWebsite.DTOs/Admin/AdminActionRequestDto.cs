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

    public class ApproveCustomCategoryDto
    {
        [MaxLength(1000)]
        public string? Notes { get; set; }

        // "category" (creates a new top-level VehicleCategory) or "subtype" (creates a
        // VehicleSubtype under an existing category) — an admin curates the raw request into one
        // of these rather than either being auto-created from arbitrary user input.
        [MaxLength(20)]
        public string ResultType { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Slug { get; set; }

        // Required when ResultType == "subtype".
        public int? VehicleCategoryId { get; set; }
    }
}