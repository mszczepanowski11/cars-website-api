using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;
using System.Net;

namespace cars_website_api.CarsWebsite.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("global")]
public class ContactController : ControllerBase
{
    private readonly IEmailService _email;
    private readonly ILogger<ContactController> _logger;

    public ContactController(IEmailService email, ILogger<ContactController> logger)
    {
        _email = email;
        _logger = logger;
    }

    [HttpPost("send")]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> Send([FromBody] ContactMessageDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) ||
            string.IsNullOrWhiteSpace(dto.Email) ||
            string.IsNullOrWhiteSpace(dto.Message))
            return BadRequest(new { message = "Wypełnij wszystkie wymagane pola." });

        if (!new EmailAddressAttribute().IsValid(dto.Email))
            return BadRequest(new { message = "Podaj prawidłowy adres e-mail." });

        if (dto.Message.Length > 5000)
            return BadRequest(new { message = "Wiadomość jest zbyt długa (max 5000 znaków)." });

        var name    = WebUtility.HtmlEncode(dto.Name.Trim());
        var email   = WebUtility.HtmlEncode(dto.Email.Trim());
        var topic   = WebUtility.HtmlEncode(dto.Topic?.Trim() ?? "—");
        var message = WebUtility.HtmlEncode(dto.Message.Trim());
        var subject = $"[CARIZO Kontakt] {topic} — {dto.Name.Trim()}";

        var html = $@"<div style=""font-family:Inter,sans-serif;max-width:600px;margin:0 auto;background:#050505;color:#e0e0e0;padding:32px;border-radius:8px;border:1px solid #1a1a1a"">
  <h2 style=""color:#8B0D1D;margin-top:0;font-size:20px"">Nowa wiadomość kontaktowa</h2>
  <table style=""width:100%;border-collapse:collapse;margin-bottom:24px;font-size:14px"">
    <tr><td style=""color:#777;padding:6px 0;width:140px;vertical-align:top"">Imię i nazwisko</td><td style=""color:#fff"">{name}</td></tr>
    <tr><td style=""color:#777;padding:6px 0;vertical-align:top"">E-mail</td><td><a href=""mailto:{email}"" style=""color:#8B0D1D"">{email}</a></td></tr>
    <tr><td style=""color:#777;padding:6px 0;vertical-align:top"">Temat</td><td style=""color:#fff"">{topic}</td></tr>
  </table>
  <div style=""background:#0d0d0d;border:1px solid #1a1a1a;border-radius:8px;padding:20px;font-size:14px;line-height:1.7"">
    <p style=""margin:0;white-space:pre-line;color:#ccc"">{message}</p>
  </div>
  <p style=""font-size:12px;color:#555;margin-top:20px"">Odpowiedz bezpośrednio: <a href=""mailto:{email}"" style=""color:#8B0D1D"">{email}</a></p>
</div>";

        try
        {
            await _email.SendAsync("kontakt@carizo.eu", subject, html);
            _logger.LogInformation("[Contact] Message from {Email} ({Name}), topic: {Topic}", dto.Email, dto.Name, dto.Topic);
            return Ok(new { message = "Wiadomość została wysłana. Odpowiemy w ciągu 24 godzin roboczych." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Contact] Failed to send message from {Email}", dto.Email);
            return StatusCode(500, new { message = "Nie udało się wysłać wiadomości. Spróbuj ponownie lub napisz bezpośrednio na kontakt@carizo.eu" });
        }
    }
}

public record ContactMessageDto(
    [Required] string Name,
    [Required] string Email,
    string? Topic,
    [Required] string Message
);
