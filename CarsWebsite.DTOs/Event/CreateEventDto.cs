namespace cars_website_api.CarsWebsite.DTOs.Event;

public class CreateEventDto
{
    public string Name { get; set; }
    public string Description { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string City { get; set; }
    public string Address { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? TicketsUrl { get; set; }
    public string? OrganizerName { get; set; }
    public string? OrganizerEmail { get; set; }
    public string? OrganizerPhone { get; set; }
}