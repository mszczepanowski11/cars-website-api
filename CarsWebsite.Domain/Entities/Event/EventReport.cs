namespace CarsWebsite;

public enum EventReportReason
{
    Spam,
    FakeEvent,
    OutdatedInfo,
    Other
}

public class EventReport
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event Event { get; set; }
    public int ReportedByUserId { get; set; }
    public User ReportedBy { get; set; }
    public EventReportReason Reason { get; set; }
    public string? Content { get; set; }
    public DateTime ReportedAt { get; set; } = DateTime.UtcNow;
    public bool IsResolved { get; set; } = false;
}