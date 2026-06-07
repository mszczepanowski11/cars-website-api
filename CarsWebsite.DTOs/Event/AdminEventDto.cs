namespace cars_website_api.CarsWebsite.DTOs.Event;

public class AdminEventDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string City { get; set; }
    public string Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CreatedByUserId { get; set; }
    public string? CreatedByName { get; set; }
    public int ReportCount { get; set; }
    public string? MainImageUrl { get; set; }
}

public class AdminEventFilterDto
{
    public string? Status { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}