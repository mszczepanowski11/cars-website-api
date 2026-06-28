using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using cars_website_api.CarsWebsite.DTOs;
using cars_website_api.CarsWebsite.DTOs.User;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace cars_website_api.CarsWebsite.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _context;
    private readonly ILogger<UserService> _logger;
    private readonly Cloudinary _cloudinary;

    public UserService(AppDbContext context, ILogger<UserService> logger, Cloudinary cloudinary)
    {
        _context = context;
        _logger = logger;
        _cloudinary = cloudinary;
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
        _logger.LogInformation("[Stats] Starting for userId={UserId}", userId);
        var dto = new UserStatsDto();

        try
        {
            _logger.LogDebug("[Stats] Querying CarAdverts for userId={UserId}", userId);
            var advertIds = await _context.CarAdverts
                .AsNoTracking()
                .Where(a => a.UserId == userId)
                .Select(a => a.Id)
                .ToListAsync();
            dto.TotalAdverts = advertIds.Count;
            _logger.LogDebug("[Stats] CarAdverts count={Count}", dto.TotalAdverts);

            dto.ActiveAdverts = await _context.CarAdverts
                .AsNoTracking()
                .CountAsync(a => a.UserId == userId && a.IsActive && !a.IsHidden);

            if (advertIds.Count > 0)
            {
                try { dto.TotalViews = await _context.AdvertViews.AsNoTracking().CountAsync(v => advertIds.Contains(v.AdvertId)); }
                catch (Exception ex) { _logger.LogWarning("[Stats] AdvertViews query failed: {Msg}", ex.Message); }
            }
        }
        catch (Exception ex) { _logger.LogWarning("[Stats] CarAdverts query failed: {Type} {Msg}", ex.GetType().Name, ex.Message); }

        try { dto.FavoritesCount = await _context.FavoriteAdverts.AsNoTracking().CountAsync(f => f.UserId == userId); }
        catch (Exception ex) { _logger.LogWarning("[Stats] FavoriteAdverts query failed: {Type} {Msg}", ex.GetType().Name, ex.Message); }

        try
        {
            _logger.LogDebug("[Stats] Querying Messages for userId={UserId}", userId);
            dto.UnreadMessages = await _context.Messages
                .AsNoTracking()
                .CountAsync(m => (m.Conversation.SellerId == userId || m.Conversation.BuyerId == userId)
                                 && !m.IsRead && m.SenderId != userId);
        }
        catch (Exception ex) { _logger.LogWarning("[Stats] Messages query failed: {Type} {Msg}", ex.GetType().Name, ex.Message); }

        try
        {
            dto.FollowersCount = await _context.UserFollows.AsNoTracking().CountAsync(f => f.FollowedId == userId);
            dto.FollowingCount = await _context.UserFollows.AsNoTracking().CountAsync(f => f.FollowerId == userId);
        }
        catch (Exception ex) { _logger.LogWarning("[Stats] UserFollows query failed: {Type} {Msg}", ex.GetType().Name, ex.Message); }

        try
        {
            _logger.LogDebug("[Stats] Querying Reviews for userId={UserId}", userId);
            var reviews = await _context.Reviews.AsNoTracking().Where(r => r.SellerId == userId).ToListAsync();
            dto.ReviewCount = reviews.Count;
            dto.AverageRating = reviews.Count > 0 ? Math.Round(reviews.Average(r => r.Rating), 2) : 0.0;
        }
        catch (Exception ex) { _logger.LogWarning("[Stats] Reviews query failed: {Type} {Msg}", ex.GetType().Name, ex.Message); }

        _logger.LogInformation("[Stats] Completed for userId={UserId}: totalAdverts={T} activeAdverts={A} views={V} favorites={F} messages={M} followers={Fo} following={Fi}",
            userId, dto.TotalAdverts, dto.ActiveAdverts, dto.TotalViews, dto.FavoritesCount, dto.UnreadMessages, dto.FollowersCount, dto.FollowingCount);
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
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            throw new ArgumentException("Hasło musi mieć co najmniej 8 znaków.");

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

        // Keep NewsletterSubscribers table in sync when user opts out
        if (!dto.NewsletterSubscribed)
        {
            var sub = await _context.NewsletterSubscribers.FirstOrDefaultAsync(s => s.Email == user.Email && s.IsActive);
            if (sub != null)
            {
                sub.IsActive = false;
                sub.UnsubscribedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();
        return dto;
    }

    public async Task DeleteAccountAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        // RODO: anonymize instead of hard delete to preserve referential integrity
        user.Email = $"deleted_{userId}_{Guid.NewGuid():N}@carizo.deleted";
        user.Name = "Usunięty";
        user.Surname = "Użytkownik";
        user.PhoneNumber = null;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString());
        user.AvatarUrl = null;
        user.City = null;
        user.Region = null;
        user.Street = null;
        user.PostalCode = null;
        user.Country = null;
        user.About = null;
        user.CompanyName = null;
        user.Nip = null;
        user.GoogleId = null;
        user.FacebookId = null;
        user.IsBlocked = true;
        user.BlockedAt = DateTime.UtcNow;
        user.BlockedReason = "Konto usunięte przez użytkownika";
        user.EmailVerified = false;
        user.EmailVerificationToken = null;
        user.EmailVerificationTokenExpires = null;
        user.PasswordResetToken = null;
        user.PasswordResetTokenExpires = null;

        // Soft-delete all adverts belonging to this user (cascade when user self-deletes account)
        var userAdverts = await _context.CarAdverts.Where(a => a.UserId == userId).ToListAsync();
        foreach (var advert in userAdverts)
        {
            advert.IsActive = false;
            advert.IsHidden = true;
            advert.UpdatedAt = DateTime.UtcNow;
        }

        var tokens = await _context.RefreshTokens
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ToListAsync();
        foreach (var t in tokens) t.IsRevoked = true;

        var advertImages = await _context.AdvertImages
            .Where(img => _context.CarAdverts
                .Where(a => a.UserId == userId)
                .Select(a => a.Id)
                .Contains(img.AdvertId))
            .ToListAsync();

        var cloudinaryTasks = advertImages
            .Select(img => ExtractPublicId(img.Url))
            .Where(pid => pid != null)
            .Select(pid => DeleteCloudinaryWithRetryAsync(pid!));
        await Task.WhenAll(cloudinaryTasks);
        _context.AdvertImages.RemoveRange(advertImages);

        // GDPR: delete all conversations and messages involving this user
        var conversationIds = await _context.Conversations
            .Where(c => c.BuyerId == userId || c.SellerId == userId)
            .Select(c => c.Id)
            .ToListAsync();
        if (conversationIds.Count > 0)
        {
            var userMessages = await _context.Messages
                .Where(m => conversationIds.Contains(m.ConversationId))
                .ToListAsync();
            _context.Messages.RemoveRange(userMessages);
            var conversations = await _context.Conversations
                .Where(c => conversationIds.Contains(c.Id))
                .ToListAsync();
            _context.Conversations.RemoveRange(conversations);
        }

        await _context.SaveChangesAsync();
    }

    public async Task<PublicUserProfileDto?> GetPublicProfileAsync(int userId)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);
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

    public async Task<object> ExportUserDataAsync(int userId)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("Użytkownik nie istnieje.");

        var adverts = await _context.CarAdverts
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => new { a.Id, a.Title, a.Price, a.CreatedAt, a.IsActive })
            .ToListAsync();

        var favorites = await _context.FavoriteAdverts
            .AsNoTracking()
            .Where(f => f.UserId == userId)
            .Select(f => new { f.AdvertId, f.CreatedAt })
            .ToListAsync();

        var sentMessages = await _context.Messages
            .AsNoTracking()
            .Where(m => m.SenderId == userId)
            .Select(m => new { m.Id, m.Content, m.SentAt, m.ConversationId })
            .ToListAsync();

        var receivedMessages = await _context.Messages
            .AsNoTracking()
            .Where(m => m.SenderId != userId && _context.Conversations
                .Any(c => c.Id == m.ConversationId && (c.BuyerId == userId || c.SellerId == userId)))
            .Select(m => new { m.Id, m.Content, m.SentAt, m.ConversationId, m.SenderId })
            .ToListAsync();

        return new
        {
            exportedAt = DateTime.UtcNow,
            profile = new
            {
                user.Id, user.Name, user.Surname, user.Email, user.PhoneNumber,
                user.AccountType, user.CompanyName, user.Nip,
                user.City, user.Region, user.Street, user.PostalCode, user.Country,
                user.About, user.AvatarUrl, user.EmailVerified,
                user.EmailNotifications, user.PriceChangeAlerts, user.NewMessageAlerts,
                user.NewsletterSubscribed, user.CreatedAt, user.LastLoginAt
            },
            adverts,
            favorites,
            sentMessages,
            receivedMessages
        };
    }

    private async Task DeleteCloudinaryWithRetryAsync(string publicId)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var result = await _cloudinary.DestroyAsync(new DeletionParams(publicId));
                if (result.Error == null) return;
                _logger.LogWarning("[UserSvc] Cloudinary delete attempt {Attempt} failed for {PublicId}: {Error}",
                    attempt + 1, publicId, result.Error.Message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[UserSvc] Cloudinary delete attempt {Attempt} threw for {PublicId}: {Msg}",
                    attempt + 1, publicId, ex.Message);
            }
            if (attempt < 2)
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
        }
        _logger.LogError("[UserSvc] Cloudinary delete failed after 3 attempts for {PublicId}", publicId);
    }

    private static string? ExtractPublicId(string url)
    {
        try
        {
            var segments = new Uri(url).AbsolutePath.Split('/');
            var uploadIdx = Array.IndexOf(segments, "upload");
            if (uploadIdx < 0) return null;
            var start = uploadIdx + 1;
            if (start < segments.Length && segments[start].StartsWith('v') && long.TryParse(segments[start][1..], out _))
                start++;
            var idWithExt = string.Join("/", segments[start..]);
            var dot = idWithExt.LastIndexOf('.');
            return dot > 0 ? idWithExt[..dot] : idWithExt;
        }
        catch { return null; }
    }
}
