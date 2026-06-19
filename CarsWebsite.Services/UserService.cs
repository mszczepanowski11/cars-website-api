using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using cars_website_api.CarsWebsite.DTOs;
using cars_website_api.CarsWebsite.DTOs.User;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace cars_website_api.CarsWebsite.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _context;
    private readonly ILogger<UserService> _logger;

    public UserService(AppDbContext context, ILogger<UserService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<User?> GetById(int id)
        => await _context.Users.FindAsync(id);

    public async Task<User?> GetByToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token))
            return null;

        var jwtToken = handler.ReadJwtToken(token);
        var userIdClaim = jwtToken.Claims
            .FirstOrDefault(c => c.Type == "nameid" || c.Type == ClaimTypes.NameIdentifier)?.Value;

        if (userIdClaim == null)
            return null;

        return await _context.Users.FindAsync(int.Parse(userIdClaim));
    }

    public async Task<UserStatsDto> GetUserStatsAsync(int userId)
    {
        var dto = new UserStatsDto();

        try
        {
            var advertIds = await _context.CarAdverts
                .Where(a => a.UserId == userId)
                .Select(a => a.Id)
                .ToListAsync();
            dto.TotalAdverts = advertIds.Count;

            dto.ActiveAdverts = await _context.CarAdverts
                .CountAsync(a => a.UserId == userId && a.IsActive && !a.IsHidden);

            if (advertIds.Count > 0)
            {
                try { dto.TotalViews = await _context.AdvertViews.CountAsync(v => advertIds.Contains(v.AdvertId)); }
                catch (Exception ex) { _logger.LogWarning("[Stats] AdvertViews query failed: {Msg}", ex.Message); }
            }
        }
        catch (Exception ex) { _logger.LogWarning("[Stats] CarAdverts query failed: {Msg}", ex.Message); }

        try { dto.FavoritesCount = await _context.FavoriteAdverts.CountAsync(f => f.UserId == userId); }
        catch (Exception ex) { _logger.LogWarning("[Stats] FavoriteAdverts query failed: {Msg}", ex.Message); }

        try
        {
            dto.UnreadMessages = await _context.Messages
                .CountAsync(m => (m.Conversation.SellerId == userId || m.Conversation.BuyerId == userId)
                                 && !m.IsRead && m.SenderId != userId);
        }
        catch (Exception ex) { _logger.LogWarning("[Stats] Messages query failed: {Msg}", ex.Message); }

        try
        {
            dto.FollowersCount = await _context.UserFollows.CountAsync(f => f.FollowedId == userId);
            dto.FollowingCount = await _context.UserFollows.CountAsync(f => f.FollowerId == userId);
        }
        catch (Exception ex) { _logger.LogWarning("[Stats] UserFollows query failed: {Msg}", ex.Message); }

        try
        {
            var reviews = await _context.Reviews.Where(r => r.SellerId == userId).ToListAsync();
            dto.ReviewCount = reviews.Count;
            dto.AverageRating = reviews.Count > 0 ? Math.Round(reviews.Average(r => r.Rating), 2) : 0.0;
        }
        catch (Exception ex) { _logger.LogWarning("[Stats] Reviews query failed: {Msg}", ex.Message); }

        return dto;
    }

    public async Task<UserStatsDto> GetPublicStatsAsync(int userId)
        => await GetUserStatsAsync(userId);

    public async Task<User> UpdateProfileAsync(int userId, UpdateProfileDto dto)
    {
        var user = await _context.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        user.Name = dto.Name;
        user.Surname = dto.Surname;
        user.PhoneNumber = dto.PhoneNumber;
        user.City = dto.City;
        user.Region = dto.Region;
        user.About = dto.About;
        user.CompanyName = dto.CompanyName;
        user.Nip = dto.Nip;
        user.Street = dto.Street;
        user.PostalCode = dto.PostalCode;
        user.Country = dto.Country;

        await _context.SaveChangesAsync();
        return user;
    }

    public async Task UpdatePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var user = await _context.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
            throw new UnauthorizedAccessException("Current password is incorrect.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _context.SaveChangesAsync();
    }

    public async Task<UserSettingsDto> GetSettingsAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        return new UserSettingsDto
        {
            EmailNotifications = user.EmailNotifications,
            PriceChangeAlerts = user.PriceChangeAlerts,
            NewMessageAlerts = user.NewMessageAlerts,
            NewsletterSubscribed = user.NewsletterSubscribed
        };
    }

    public async Task<UserSettingsDto> UpdateSettingsAsync(int userId, UserSettingsDto dto)
    {
        var user = await _context.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        user.EmailNotifications = dto.EmailNotifications;
        user.PriceChangeAlerts = dto.PriceChangeAlerts;
        user.NewMessageAlerts = dto.NewMessageAlerts;
        user.NewsletterSubscribed = dto.NewsletterSubscribed;

        await _context.SaveChangesAsync();
        return dto;
    }

    public async Task DeleteAccountAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();
    }

    public async Task<PublicUserProfileDto?> GetPublicProfileAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return null;

        return new PublicUserProfileDto
        {
            Id = user.Id,
            Name = user.Name,
            Surname = user.Surname,
            AvatarUrl = user.AvatarUrl,
            City = user.City,
            Region = user.Region,
            About = user.About,
            AccountType = user.AccountType.ToString(),
            CompanyName = user.CompanyName,
            CreatedAt = user.CreatedAt,
            EmailVerified = user.EmailVerified
        };
    }
}
