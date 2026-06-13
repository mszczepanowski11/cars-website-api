using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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

    public async Task<object?> Register(RegisterDto dto)
    {
        if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
            return null;

        var user = new User
        {
            Name = dto.Name,
            Surname = dto.Surname,
            Email = dto.Email,
            PhoneNumber = dto.PhoneNumber,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return new { token = GenerateToken(user) };
    }

    public async Task<object?> Login(LoginDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);

        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return null;

        if (user.IsBlocked)
            return null;

        return new { token = GenerateToken(user) };
    }

    public Task<object?> RefreshAsync(string refreshToken)
        => Task.FromResult<object?>(null);

    public Task RevokeAsync(string refreshToken)
        => Task.CompletedTask;

    public Task ForgotPasswordAsync(string email)
        => Task.CompletedTask;

    public Task<bool> ResetPasswordAsync(string token, string newPassword)
        => Task.FromResult(false);

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
