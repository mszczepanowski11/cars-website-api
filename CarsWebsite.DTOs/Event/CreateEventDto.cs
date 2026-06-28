using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs.Event;

public class CreateEventDto
{
    [Required][MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required][MaxLength(10000)] public string Description { get; set; } = string.Empty;
    [Required] public DateTime StartDate { get; set; }
    [Required] public DateTime EndDate { get; set; }
    [Required][MaxLength(100)] public string City { get; set; } = string.Empty;
    [Required][MaxLength(300)] public string Address { get; set; } = string.Empty;
    [Url][MaxLength(500)] public string? WebsiteUrl { get; set; }
    [Url][MaxLength(500)] public string? TicketsUrl { get; set; }
    [MaxLength(200)] public string? OrganizerName { get; set; }
    [EmailAddress][MaxLength(256)] public string? OrganizerEmail { get; set; }
    [MaxLength(20)] public string? OrganizerPhone { get; set; }
}