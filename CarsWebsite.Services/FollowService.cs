using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Services;

public class FollowService : IFollowService
{
    private readonly AppDbContext _context;

    public FollowService(AppDbContext context)
    {
        _context = context;
    }

    public async Task FollowAsync(int followerId, int followedId)
    {
        if (followerId == followedId) return;
        var exists = await _context.UserFollows.AsNoTracking().AnyAsync(f => f.FollowerId == followerId && f.FollowedId == followedId);
        if (exists) return;

        _context.UserFollows.Add(new UserFollow { FollowerId = followerId, FollowedId = followedId });
        await _context.SaveChangesAsync();
    }

    public async Task UnfollowAsync(int followerId, int followedId)
    {
        var follow = await _context.UserFollows.FirstOrDefaultAsync(f => f.FollowerId == followerId && f.FollowedId == followedId);
        if (follow == null) return;

        _context.UserFollows.Remove(follow);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> IsFollowingAsync(int followerId, int followedId)
        => await _context.UserFollows.AsNoTracking().AnyAsync(f => f.FollowerId == followerId && f.FollowedId == followedId);
}
