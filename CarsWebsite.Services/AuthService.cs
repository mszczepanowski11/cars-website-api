using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using cars_website_api.CarsWebsite.DTOs;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace cars_website_api.CarsWebsite.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IEmailService _email;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AuthService> _logger;

    public AuthService(AppDbContext context, IConfiguration configuration, IEmailService email, IHttpClientFactory httpClientFactory, ILogger<AuthService> logger)
    {
        _context = context;
        _configuration = configuration;
        _email = email;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private static void ValidatePasswordStrength(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new ArgumentException("Hasło musi mieć co najmniej 8 znaków.");
    }

    public async Task<object?> Register(RegisterDto dto)
    {
        ValidatePasswordStrength(dto.Password);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var age = today.Year - dto.DateOfBirth.Year;
        if (dto.DateOfBirth.AddYears(age) > today) age--;
        if (age < 18)
            throw new InvalidOperationException("Musisz mieć ukończone 18 lat, aby założyć konto.");

        var normalizedEmail = (dto.Email ?? "").Trim().ToLowerInvariant();

        if (await _context.Users.AnyAsync(u => u.Email == normalizedEmail))
            return null;

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        var user = new User
        {
            Name = dto.Name,
            Surname = dto.Surname,
            Email = normalizedEmail,
            PhoneNumber = dto.PhoneNumber,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            DateOfBirth = dto.DateOfBirth.ToDateTime(TimeOnly.MinValue),
            AccountType = dto.AccountType,
            BusinessType = dto.BusinessType,
            CompanyName = dto.CompanyName,
            Nip = dto.Nip,
            EmailVerified = false,
            EmailVerificationToken = token,
            EmailVerificationTokenExpires = DateTime.UtcNow.AddHours(24),
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var siteUrl = _configuration["SiteUrl"] ?? "https://carizo.eu";
        var subject = "Potwierdź swój adres email – CARIZO";
        var html = EmailService.BuildHtml(
            "Potwierdź adres email",
            "Kliknij poniższy przycisk, aby aktywować konto CARIZO. Link jest ważny przez 24 godziny.",
            null,
            $"{siteUrl}/weryfikacja-email?token={token}",
            "Aktywuj konto");

        // Fire-and-forget — don't block HTTP response on SMTP; log failures asynchronously.
        _ = _email.SendAsync(user.Email, subject, html)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _logger.LogError(t.Exception, "[Register] Email wysyłki nie powiódł się dla {Email}", user.Email);
            }, TaskContinuationOptions.OnlyOnFaulted);

        return new { message = "Rejestracja zakończona. Sprawdź skrzynkę email." };
    }

    public async Task<object?> Login(LoginDto dto)
    {
        var normalizedEmail = (dto.Email ?? "").Trim().ToLowerInvariant();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);

        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return null;

        if (user.IsBlocked)
            return new { error = "blocked" };

        if (!user.EmailVerified)
            return new { error = "unverified" };

        user.LastLoginAt = DateTime.UtcNow;

        // Clean up expired/revoked tokens for this user to prevent unbounded table growth.
        // ExecuteDeleteAsync generates a direct DELETE without SELECTing the full entity,
        // so it is resilient to missing nullable columns (e.g. RevokedAt during migration).
        await _context.RefreshTokens
            .Where(t => t.UserId == user.Id && (t.IsRevoked || t.ExpiresAt <= DateTime.UtcNow))
            .ExecuteDeleteAsync();

        await _context.SaveChangesAsync();
        return await IssueTokenPairAsync(user);
    }

    public async Task<object?> RefreshAsync(string refreshToken)
    {
        var record = await _context.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == refreshToken);

        if (record == null || record.IsRevoked || record.ExpiresAt <= DateTime.UtcNow)
            return null;

        if (record.User.IsBlocked) return new { error = "blocked" };

        record.IsRevoked = true;
        record.RevokedAt = DateTime.UtcNow;
        var newPair = await IssueTokenPairAsync(record.User);
        await _context.SaveChangesAsync();
        return newPair;
    }

    public async Task RevokeAsync(string refreshToken)
    {
        var record = await _context.RefreshTokens.FirstOrDefaultAsync(t => t.Token == refreshToken);
        if (record == null) return;
        record.IsRevoked = true;
        record.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    private async Task<object> IssueTokenPairAsync(User user)
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _context.RefreshTokens.Add(new RefreshToken
        {
            Token = raw,
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        return new { token = GenerateToken(user), refreshToken = raw };
    }

    public async Task ForgotPasswordAsync(string email)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null) return;

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        user.PasswordResetToken = token;
        user.PasswordResetTokenExpires = DateTime.UtcNow.AddHours(1);
        await _context.SaveChangesAsync();

        var siteUrl = _configuration["SiteUrl"] ?? "https://carizo.eu";
        var html = EmailService.BuildHtml("Resetowanie hasła",
            "Kliknij poniższy przycisk, aby ustawić nowe hasło. Link jest ważny przez 1 godzinę.",
            null,
            $"{siteUrl}/reset-password?token={token}",
            "Resetuj hasło");

        _ = _email.SendAsync(user.Email, "Resetowanie hasła – CARIZO", html)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _logger.LogError(t.Exception, "[ForgotPassword] Email wysyłki nie powiódł się dla {Email}", user.Email);
            }, TaskContinuationOptions.OnlyOnFaulted);
    }

    public async Task<bool> ResetPasswordAsync(string token, string newPassword)
    {
        ValidatePasswordStrength(newPassword);

        var user = await _context.Users.FirstOrDefaultAsync(u => u.PasswordResetToken == token);
        if (user == null || user.PasswordResetTokenExpires < DateTime.UtcNow) return false;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.PasswordResetToken = null;
        user.PasswordResetTokenExpires = null;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> VerifyEmailAsync(string token)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.EmailVerificationToken == token);
        if (user == null || user.EmailVerificationTokenExpires < DateTime.UtcNow) return false;
        user.EmailVerified = true;
        user.EmailVerificationToken = null;
        user.EmailVerificationTokenExpires = null;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task ResendVerificationAsync(string email)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email && !u.EmailVerified);
        if (user == null) return;

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        user.EmailVerificationToken = token;
        user.EmailVerificationTokenExpires = DateTime.UtcNow.AddHours(24);
        await _context.SaveChangesAsync();

        var siteUrl = _configuration["SiteUrl"] ?? "https://carizo.eu";
        var subject = "Potwierdź swój adres email – CARIZO";
        var html = EmailService.BuildHtml(
            "Potwierdź adres email",
            "Kliknij poniższy przycisk, aby aktywować konto CARIZO. Link jest ważny przez 24 godziny.",
            null,
            $"{siteUrl}/weryfikacja-email?token={token}",
            "Aktywuj konto");

        _ = _email.SendAsync(user.Email, subject, html)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _logger.LogError(t.Exception, "[ResendVerification] Email wysyłki nie powiódł się dla {Email}", user.Email);
            }, TaskContinuationOptions.OnlyOnFaulted);
    }

    public async Task<object?> GoogleLoginAsync(string credential)
    {
        var httpClient = _httpClientFactory.CreateClient();
        GoogleTokenPayload? payload;
        try
        {
            payload = await httpClient.GetFromJsonAsync<GoogleTokenPayload>(
                $"https://oauth2.googleapis.com/tokeninfo?id_token={Uri.EscapeDataString(credential)}");
        }
        catch { return null; }

        if (payload == null || string.IsNullOrEmpty(payload.Email) || payload.EmailVerified != "true")
            return null;

        // Validate audience — ClientId must be configured, otherwise Google login is disabled
        var clientId = _configuration["Google:ClientId"]
            ?? Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
        if (string.IsNullOrEmpty(clientId))
        {
            _logger.LogError("[GoogleLogin] Google:ClientId nie skonfigurowany — logowanie przez Google wyłączone.");
            return null;
        }
        if (payload.Aud != clientId) return null;

        var user = await _context.Users.FirstOrDefaultAsync(u => u.GoogleId == payload.Sub)
                ?? await _context.Users.FirstOrDefaultAsync(u => u.Email == payload.Email);

        if (user == null)
        {
            user = new User
            {
                Name = payload.GivenName ?? "Użytkownik",
                Surname = payload.FamilyName ?? "Google",
                Email = payload.Email,
                PhoneNumber = string.Empty,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),
                EmailVerified = true,
                GoogleId = payload.Sub,
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
        }
        else if (user.GoogleId == null)
        {
            user.GoogleId = payload.Sub;
            if (!user.EmailVerified) user.EmailVerified = true;
            await _context.SaveChangesAsync();
        }

        if (user.IsBlocked) return new { error = "blocked" };

        user.LastLoginAt = DateTime.UtcNow;
        return await IssueTokenPairAsync(user);
    }

    private static string GenerateRefreshToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private string GenerateToken(User user)
    {
        // JWT_SECRET_KEY env var takes precedence over appsettings Jwt:Key (which may be empty).
        // This mirrors the same lookup used at startup in Program.cs.
        var jwtKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
            ?? _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("JWT key is not configured (set JWT_SECRET_KEY or Jwt:Key).");
        if (string.IsNullOrEmpty(jwtKey))
            throw new InvalidOperationException("JWT key is empty — set JWT_SECRET_KEY environment variable.");

        var jwtIssuer = _configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
        var jwtAudience = _configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience is not configured.");
        var expiresInMinutes = double.Parse(_configuration["Jwt:ExpiresInMinutes"] ?? throw new InvalidOperationException("Jwt:ExpiresInMinutes is not configured."));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("isAdmin", user.IsAdmin ? "true" : "false")
        };

        var token = new JwtSecurityToken(
            issuer: jwtIssuer,
            audience: jwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiresInMinutes),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<object?> FacebookLoginAsync(string accessToken)
    {
        var appSecret = _configuration["Facebook:AppSecret"]
            ?? Environment.GetEnvironmentVariable("FACEBOOK_APP_SECRET") ?? "";
        if (string.IsNullOrEmpty(appSecret))
        {
            _logger.LogError("[FacebookLogin] Facebook:AppSecret nie skonfigurowany — logowanie wyłączone.");
            return null;
        }
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(appSecret));
        var proof = Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(accessToken))).ToLowerInvariant();
        var graphUrl = $"https://graph.facebook.com/me?fields=id,name,first_name,last_name,email&access_token={Uri.EscapeDataString(accessToken)}&appsecret_proof={proof}";

        var httpClient = _httpClientFactory.CreateClient();
        FacebookUserPayload? payload;
        try
        {
            payload = await httpClient.GetFromJsonAsync<FacebookUserPayload>(graphUrl);
        }
        catch { return null; }

        if (payload == null || string.IsNullOrEmpty(payload.Id))
            return null;

        var user = await _context.Users.FirstOrDefaultAsync(u => u.FacebookId == payload.Id)
                ?? (!string.IsNullOrEmpty(payload.Email)
                    ? await _context.Users.FirstOrDefaultAsync(u => u.Email == payload.Email)
                    : null);

        if (user == null)
        {
            if (string.IsNullOrEmpty(payload.Email))
                return null;

            user = new User
            {
                Name = payload.FirstName ?? payload.Name?.Split(' ').FirstOrDefault() ?? "Użytkownik",
                Surname = payload.LastName ?? (payload.Name?.Contains(' ') == true ? payload.Name.Substring(payload.Name.IndexOf(' ') + 1) : "Facebook"),
                Email = payload.Email,
                PhoneNumber = string.Empty,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),
                EmailVerified = true,
                FacebookId = payload.Id,
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
        }
        else if (user.FacebookId == null)
        {
            user.FacebookId = payload.Id;
            if (!user.EmailVerified) user.EmailVerified = true;
            await _context.SaveChangesAsync();
        }

        if (user.IsBlocked) return new { error = "blocked" };

        user.LastLoginAt = DateTime.UtcNow;
        return await IssueTokenPairAsync(user);
    }

    private sealed class GoogleTokenPayload
    {
        [JsonPropertyName("sub")]          public string Sub          { get; set; } = string.Empty;
        [JsonPropertyName("email")]        public string? Email       { get; set; }
        [JsonPropertyName("email_verified")] public string? EmailVerified { get; set; }
        [JsonPropertyName("given_name")]   public string? GivenName   { get; set; }
        [JsonPropertyName("family_name")]  public string? FamilyName  { get; set; }
        [JsonPropertyName("aud")]          public string? Aud         { get; set; }
    }

    private sealed class FacebookUserPayload
    {
        [JsonPropertyName("id")]         public string? Id        { get; set; }
        [JsonPropertyName("name")]       public string? Name      { get; set; }
        [JsonPropertyName("first_name")] public string? FirstName { get; set; }
        [JsonPropertyName("last_name")]  public string? LastName  { get; set; }
        [JsonPropertyName("email")]      public string? Email     { get; set; }
    }
}
