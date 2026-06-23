namespace cars_website_api.CarsWebsite.Domain.Entities;

public class CustomCategoryRequest
{
    public int Id { get; set; }
    public string? UserId { get; set; } // nullable for anonymous
    public string CategoryName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ParametersJson { get; set; } // JSON string of user-defined params
    public string Status { get; set; } = "Pending"; // "Pending" | "Approved" | "Rejected"
    public string? AdminNotes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
}
