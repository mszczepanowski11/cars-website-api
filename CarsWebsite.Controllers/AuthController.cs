using cars_website_api.CarsWebsite.DTOs;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace cars_website_api.CarsWebsite.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        var result = await _authService.Register(dto);
        if (result == null)
            return Conflict("An account with this email already exists.");

        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var result = await _authService.Login(dto);
        if (result == null)
            return Unauthorized("Błędne dane logowania lub konto zablokowane.");

        return Ok(result);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenDto dto)
    {
        var result = await _authService.RefreshAsync(dto.RefreshToken);
        if (result == null)
            return Unauthorized("Nieprawidłowy lub wygasły refresh token.");

        return Ok(result);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenDto dto)
    {
        await _authService.RevokeAsync(dto.RefreshToken);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        // Always return 200 to avoid user enumeration
        await _authService.ForgotPasswordAsync(dto.Email);
        return Ok(new { message = "Jeśli konto istnieje, wysłaliśmy link resetujący." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Token) || string.IsNullOrWhiteSpace(dto.NewPassword))
            return BadRequest("Brakujące dane.");

        if (dto.NewPassword.Length < 8)
            return BadRequest("Hasło musi mieć co najmniej 8 znaków.");

        var success = await _authService.ResetPasswordAsync(dto.Token, dto.NewPassword);
        if (!success)
            return BadRequest("Link wygasł lub jest nieprawidłowy.");

        return Ok(new { message = "Hasło zostało zmienione." });
    }
}