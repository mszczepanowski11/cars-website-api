namespace cars_website_api.CarsWebsite.Domain.Entities;

public enum CustomCategoryRequestStatus
{
    Pending,
    Approved,
    Rejected,
}

public class CustomCategoryRequest
{
    public int Id { get; set; }
    public string? UserId { get; set; } // nullable for anonymous
    public string CategoryName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ParametersJson { get; set; } // JSON string of user-defined params
    public CustomCategoryRequestStatus Status { get; set; } = CustomCategoryRequestStatus.Pending;
    public string? AdminNotes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }

    // Set on approval — the real taxonomy row an admin curated from this request. Exactly one of
    // these is populated depending on whether the admin approved it as a brand-new top-level
    // category or as a subtype within an existing one (see AdminController.ApproveCustomCategory).
    public int? ResultingVehicleCategoryId { get; set; }
    public VehicleCategory? ResultingVehicleCategory { get; set; }
    public int? ResultingVehicleSubtypeId { get; set; }
    public VehicleSubtype? ResultingVehicleSubtype { get; set; }
}
