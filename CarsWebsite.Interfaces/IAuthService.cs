using cars_website_api.CarsWebsite.DTOs;

namespace cars_website_api.CarsWebsite.Interfaces;

public interface IAuthService
{
    Task<object?> Register(RegisterDto dto);
    Task<object?> Login(LoginDto dto);
    Task<object?> RefreshAsync(string refreshToken);
    Task RevokeAsync(string refreshToken);
    Task ForgotPasswordAsync(string email);
    Task<bool> ResetPasswordAsync(string token, string newPassword);
    Task<bool> VerifyEmailAsync(string token);
    Task ResendVerificationAsync(string email);
    Task<object?> GoogleLoginAsync(string credential);
    Task<object?> FacebookLoginAsync(string accessToken, bool consentGiven, string? ipAddress, string? userAgent);
}
