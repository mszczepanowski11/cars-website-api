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

    public AuthService(AppDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    public async Task<AuthResponseDto?> Register(RegisterDto dto)
    {
        if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
            return null;

        var user = new User
        {
            Name = dto.Name,
            Surname = dto.Surname,
            Email = dto.Email,
            PhoneNumber = dto.PhoneNumber,
            AccountType = dto.AccountType,
            CompanyName = dto.CompanyName,
            Nip = dto.Nip,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return await BuildAuthResponse(user);
    }

    public async Task<AuthResponseDto?> Login(LoginDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);

        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return null;

        if (user.IsBlocked)
            return null;

        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return await BuildAuthResponse(user);
    }

    public async Task<AuthResponseDto?> RefreshAsync(string refreshToken)
    {
        var stored = await _context.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == refreshToken);

        if (stored == null || stored.IsRevoked || stored.ExpiresAt < DateTime.UtcNow)
            return null;

        if (stored.User.IsBlocked)
            return null;

        stored.IsRevoked = true;
        await _context.SaveChangesAsync();

        return await BuildAuthResponse(stored.User);
    }

    public async Task RevokeAsync(string refreshToken)
    {
        var stored = await _context.RefreshTokens.FirstOrDefaultAsync(r => r.Token == refreshToken);
        if (stored != null)
        {
            stored.IsRevoked = true;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<string?> ForgotPasswordAsync(string email)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null) return null;

        // Invalidate any existing tokens for this user
        var existing = _context.PasswordResetTokens
            .Where(t => t.UserId == user.Id && !t.IsUsed && t.ExpiresAt > DateTime.UtcNow);
        await existing.ForEachAsync(t => t.IsUsed = true);

        var resetToken = new PasswordResetToken
        {
            UserId = user.Id,
            Token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48))
                .Replace("+", "-").Replace("/", "_").Replace("=", ""),
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        _context.PasswordResetTokens.Add(resetToken);
        await _context.SaveChangesAsync();

        // In production: send email with reset link
        Console.WriteLine($"[PASSWORD RESET] Token for {email}: {resetToken.Token}");
        return resetToken.Token;
    }

    public async Task<bool> ResetPasswordAsync(string token, string newPassword)
    {
        var record = await _context.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == token && !t.IsUsed && t.ExpiresAt > DateTime.UtcNow);

        if (record == null) return false;

        record.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        record.IsUsed = true;

        // Revoke all refresh tokens on password reset
        var refreshTokens = _context.RefreshTokens.Where(r => r.UserId == record.UserId);
        await refreshTokens.ForEachAsync(r => r.IsRevoked = true);

        await _context.SaveChangesAsync();
        return true;
    }

    private async Task<AuthResponseDto> BuildAuthResponse(User user)
    {
        var refreshTokenDays = int.Parse(_configuration["Jwt:RefreshTokenDays"] ?? "30");

        var newRefreshToken = new RefreshToken
        {
            UserId = user.Id,
            Token = GenerateSecureToken(),
            ExpiresAt = DateTime.UtcNow.AddDays(refreshTokenDays)
        };
        _context.RefreshTokens.Add(newRefreshToken);
        await _context.SaveChangesAsync();

        return new AuthResponseDto
        {
            Token = GenerateJwt(user),
            RefreshToken = newRefreshToken.Token
        };
    }

    private string GenerateJwt(User user)
    {
        var jwtKey = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");
        var jwtIssuer = _configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
        var jwtAudience = _configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience is not configured.");
        var expiresInMinutes = double.Parse(_configuration["Jwt:ExpiresInMinutes"] ?? throw new InvalidOperationException("Jwt:ExpiresInMinutes is not configured."));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email)
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

    private static string GenerateSecureToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
}