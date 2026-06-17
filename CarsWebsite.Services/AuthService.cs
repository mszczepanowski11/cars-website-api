using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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

    public AuthService(AppDbContext context, IConfiguration configuration, IEmailService email)
    {
        _context = context;
        _configuration = configuration;
        _email = email;
    }

    public async Task<object?> Register(RegisterDto dto)
    {
        if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
            return null;

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        var user = new User
        {
            Name = dto.Name,
            Surname = dto.Surname,
            Email = dto.Email,
            PhoneNumber = dto.PhoneNumber,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            EmailVerified = false,
            EmailVerificationToken = token,
            EmailVerificationTokenExpires = DateTime.UtcNow.AddHours(24),
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var siteUrl = _configuration["SiteUrl"] ?? "https://carizo.pl";
        await _email.SendAsync(
            user.Email,
            "Potwierdź swój adres email – CARIZO",
            EmailService.BuildHtml(
                "Potwierdź adres email",
                "Kliknij poniższy przycisk, aby aktywować konto CARIZO.",
                null,
                $"{siteUrl}/weryfikacja-email?token={token}",
                "Aktywuj konto"));

        return new { message = "Rejestracja zakończona. Sprawdź skrzynkę email." };
    }

    public async Task<object?> Login(LoginDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);

        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return null;

        if (user.IsBlocked)
            return new { error = "blocked" };

        if (!user.EmailVerified)
            return new { error = "unverified" };

        return new { token = GenerateToken(user) };
    }

    public Task<object?> RefreshAsync(string refreshToken)
        => Task.FromResult<object?>(null);

    public Task RevokeAsync(string refreshToken)
        => Task.CompletedTask;

    public async Task ForgotPasswordAsync(string email)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null) return; // don't reveal if user exists

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        user.PasswordResetToken = token;
        user.PasswordResetTokenExpires = DateTime.UtcNow.AddHours(1);
        await _context.SaveChangesAsync();

        var siteUrl = _configuration["SiteUrl"] ?? "https://carizo.pl";
        await _email.SendAsync(user.Email, "Resetowanie hasła – CARIZO",
            EmailService.BuildHtml("Resetowanie hasła",
                "Kliknij poniższy przycisk, aby ustawić nowe hasło. Link jest ważny przez 1 godzinę.",
                null,
                $"{siteUrl}/reset-hasla?token={token}",
                "Resetuj hasło"));
    }

    public async Task<bool> ResetPasswordAsync(string token, string newPassword)
    {
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

        var siteUrl = _configuration["SiteUrl"] ?? "https://carizo.pl";
        await _email.SendAsync(
            user.Email,
            "Potwierdź swój adres email – CARIZO",
            EmailService.BuildHtml(
                "Potwierdź adres email",
                "Kliknij poniższy przycisk, aby aktywować konto CARIZO.",
                null,
                $"{siteUrl}/weryfikacja-email?token={token}",
                "Aktywuj konto"));
    }

    private string GenerateToken(User user)
    {
        var jwtKey = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");
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
}
