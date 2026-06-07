using CarsWebsite;

namespace cars_website_api.CarsWebsite.DTOs.Event;

public class CreateEventReportDto
{
    public EventReportReason Reason { get; set; }
    public string? Content { get; set; }
}