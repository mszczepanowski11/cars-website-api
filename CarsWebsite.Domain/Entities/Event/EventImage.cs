namespace CarsWebsite;

public class EventImage
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event Event { get; set; }
    public string Url { get; set; }
    public bool IsMain { get; set; } = false;
}