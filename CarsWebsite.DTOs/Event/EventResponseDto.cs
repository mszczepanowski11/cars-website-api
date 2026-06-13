namespace cars_website_api.CarsWebsite.DTOs.Event;

public class EventResponseDto
{
    public int Id { get; set; }
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
    public string Status { get; set; }
    public bool IsFeatured { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CreatedByUserId { get; set; }
    public string? CreatedByName { get; set; }
    public List<EventImageDto> Images { get; set; } = new();
    public int AttendingCount { get; set; }
    public int InterestedCount { get; set; }
    public bool IsUserInterested { get; set; }
    public bool IsUserFavorite { get; set; }
}

public class EventImageDto
{
    public int Id { get; set; }
    public string Url { get; set; }
    public bool IsMain { get; set; }
}
