using CarsWebsite;

namespace cars_website_api.CarsWebsite.Domain.Entities;

public class UserFollow
{
    public int Id { get; set; }
    public int FollowerId { get; set; }
    public User Follower { get; set; } = null!;
    public int FollowedId { get; set; }
    public User Followed { get; set; } = null!;
    public DateTime FollowedAt { get; set; } = DateTime.UtcNow;
}
