namespace cars_website_api.CarsWebsite.Interfaces;

public interface IFollowService
{
    Task FollowAsync(int followerId, int followedId);
    Task UnfollowAsync(int followerId, int followedId);
    Task<bool> IsFollowingAsync(int followerId, int followedId);
}
