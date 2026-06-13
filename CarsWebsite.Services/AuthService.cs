using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using cars_website_api.CarsWebsite.DTOs;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;


namespace cars_website_api.CarsWebsite.Services;

public class AuthService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly INotificationService _notifications;

    public AuthService(AppDbContext context, IConfiguration configuration, INotificationService notifications)
    {
        _context = context;
        _configuration = configuration;
        _notifications = notifications;
    }

    public async Task<string?> Register(RegisterDto dto)
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

        _ = _notifications.NotifyAsync(user.Id, EmailNotificationType.AccountCreated,
            "Witamy w CARIZO!",
            $"Cześć {user.Name}! Twoje konto zostało pomyślnie utworzone. Możesz teraz dodawać ogłoszenia i korzystać z pełni możliwości serwisu.");

        return GenerateToken(user);
    }

    public async Task<string?> Login(LoginDto dto)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == dto.Email);

        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return null;

        if (user.IsBlocked)
            return null;

        return GenerateToken(user);
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