namespace CarsWebsite;

public enum EventStatus
{
    Pending,
    Published,
    Rejected,
    Archived
}

public class Event
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
    public EventStatus Status { get; set; } = EventStatus.Pending;
    public bool IsFeatured { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public int CreatedByUserId { get; set; }
    public User CreatedBy { get; set; }
    public ICollection<EventImage> Images { get; set; } = new List<EventImage>();
    public ICollection<EventReport> Reports { get; set; } = new List<EventReport>();
}
