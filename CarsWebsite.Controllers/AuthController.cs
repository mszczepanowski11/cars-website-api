using cars_website_api.CarsWebsite.DTOs;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace cars_website_api.CarsWebsite.Controllers;

[EnableRateLimiting("auth")]
[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        try
        {
            var result = await _authService.Register(dto);
            if (result == null)
                return Conflict(new { message = "Konto z tym adresem email już istnieje." });
            return StatusCode(201, result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        object? result;
        try
        {
            result = await _authService.Login(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Auth/Login] Unhandled exception: {Message}", ex.Message);
            return StatusCode(500, new { message = "Błąd serwera podczas logowania." });
        }

        if (result == null)
            return Unauthorized("Błędne dane logowania.");

        // Check for error objects returned instead of a token
        var resultType = result.GetType();
        var errorProp = resultType.GetProperty("error");
        if (errorProp != null)
        {
            var errorVal = errorProp.GetValue(result)?.ToString();
            return errorVal switch
            {
                "unverified" => Unauthorized("Zweryfikuj swój adres email przed zalogowaniem."),
                "blocked"    => Unauthorized("Konto zostało zablokowane."),
                _            => Unauthorized("Błędne dane logowania.")
            };
        }

        return Ok(result);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenDto dto)
    {
        var result = await _authService.RefreshAsync(dto.RefreshToken);
        if (result == null)
            return Unauthorized("Nieprawidłowy lub wygasły refresh token.");

        var resultType = result.GetType();
        var errorProp = resultType.GetProperty("error");
        if (errorProp != null)
        {
            var errorVal = errorProp.GetValue(result)?.ToString();
            if (errorVal == "blocked")
                return Unauthorized("Konto zostało zablokowane.");
        }

        return Ok(result);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenDto dto)
    {
        await _authService.RevokeAsync(dto.RefreshToken);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        // Always return 200 to avoid user enumeration
        await _authService.ForgotPasswordAsync(dto.Email);
        return Ok(new { message = "Jeśli konto istnieje, wysłaliśmy link resetujący." });
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Token) || string.IsNullOrWhiteSpace(dto.NewPassword))
            return BadRequest("Brakujące dane.");

        if (dto.NewPassword.Length < 8 || dto.NewPassword.Length > 128)
            return BadRequest("Hasło musi mieć od 8 do 128 znaków.");

        var success = await _authService.ResetPasswordAsync(dto.Token, dto.NewPassword);
        if (!success)
            return BadRequest("Link wygasł lub jest nieprawidłowy.");

        return Ok(new { message = "Hasło zostało zmienione." });
    }

    [HttpGet("verify-email")]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> VerifyEmail([FromQuery] string token)
    {
        var ok = await _authService.VerifyEmailAsync(token);
        return ok ? Ok(new { message = "Email zweryfikowany. Możesz się teraz zalogować." }) : BadRequest("Link wygasł lub jest nieprawidłowy.");
    }

    [HttpPost("resend-verification")]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> ResendVerification([FromBody] ForgotPasswordDto dto)
    {
        await _authService.ResendVerificationAsync(dto.Email);
        return Ok(new { message = "Jeśli konto istnieje i nie jest zweryfikowane, wysłaliśmy nowy link." });
    }

    [HttpPost("google")]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginDto dto)
    {
        var result = await _authService.GoogleLoginAsync(dto.Credential);
        if (result == null) return Unauthorized("Nie można zalogować przez Google.");

        var resultType = result.GetType();
        var errorProp = resultType.GetProperty("error");
        if (errorProp != null)
        {
            var errorVal = errorProp.GetValue(result)?.ToString();
            return errorVal == "blocked"
                ? Unauthorized("Konto zostało zablokowane.")
                : Unauthorized("Nie można zalogować przez Google.");
        }

        return Ok(result);
    }

    [HttpPost("facebook")]
    public async Task<IActionResult> FacebookLogin([FromBody] FacebookLoginDto dto)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        var result = await _authService.FacebookLoginAsync(dto.AccessToken, dto.ConsentGiven, ip, userAgent);
        if (result == null) return Unauthorized("Nie można zalogować przez Facebook.");

        var resultType = result.GetType();
        var errorProp = resultType.GetProperty("error");
        if (errorProp != null)
        {
            var errorVal = errorProp.GetValue(result)?.ToString();
            if (errorVal == "consent_required")
            {
                return Ok(new
                {
                    consentRequired = true,
                    name = resultType.GetProperty("name")?.GetValue(result)?.ToString(),
                    email = resultType.GetProperty("email")?.GetValue(result)?.ToString(),
                });
            }
            return errorVal == "blocked"
                ? Unauthorized("Konto zostało zablokowane.")
                : Unauthorized("Nie można zalogować przez Facebook.");
        }

        return Ok(result);
    }
}
