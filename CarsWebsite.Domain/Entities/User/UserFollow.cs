namespace cars_website_api.CarsWebsite.Domain.Entities;

public class UserFollow
{
    public int Id { get; set; }
    public int FollowerId { get; set; }
    public int FollowedId { get; set; }
    public DateTime FollowedAt { get; set; } = DateTime.UtcNow;
}
