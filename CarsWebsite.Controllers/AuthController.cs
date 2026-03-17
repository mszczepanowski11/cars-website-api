using cars_website_api.CarsWebsite.DTOs;
using cars_website_api.CarsWebsite.Services;
using Microsoft.AspNetCore.Mvc;

namespace cars_website_api.CarsWebsite.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController : Controller
{
    private readonly AuthService _authService;
    
    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        var token = await _authService.Register(dto);
        
        return Ok();
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var token = await _authService.Login(dto);
        if (token == null)
            return Unauthorized("Błędne dane logowania");

        return Ok(new { token });
    }
}