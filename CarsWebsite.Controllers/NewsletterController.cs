using CarsWebsite;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;

namespace cars_website_api.CarsWebsite.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("global")]
public class NewsletterController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IEmailService _email;
    private readonly IConfiguration _config;
    private readonly ILogger<NewsletterController> _logger;

    public NewsletterController(AppDbContext context, IEmailService email, IConfiguration config, ILogger<NewsletterController> logger)
    {
        _context = context;
        _email = email;
        _config = config;
        _logger = logger;
    }

    [HttpPost("subscribe")]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> Subscribe([FromBody] NewsletterSubscribeDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || !new EmailAddressAttribute().IsValid(dto.Email))
            return BadRequest(new { message = "Podaj prawidłowy adres e-mail." });

        var email = dto.Email.Trim().ToLowerInvariant();
        var existing = await _context.NewsletterSubscribers.FirstOrDefaultAsync(n => n.Email == email);

        if (existing != null && existing.IsConfirmed && existing.IsActive)
            return Ok(new { message = "Ten adres e-mail jest już zapisany do newslettera." });

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var expires = DateTime.UtcNow.AddHours(24);

        if (existing != null)
        {
            existing.ConfirmationToken = token;
            existing.ConfirmationTokenExpires = expires;
            existing.IsConfirmed = false;
            existing.IsActive = false;
            existing.SubscribedAt = DateTime.UtcNow;
            existing.UnsubscribedAt = null;
        }
        else
        {
            _context.NewsletterSubscribers.Add(new NewsletterSubscriber
            {
                Email = email,
                IsActive = false,
                IsConfirmed = false,
                ConfirmationToken = token,
                ConfirmationTokenExpires = expires,
                SubscribedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        var siteUrl = _config["SiteUrl"] ?? "https://carizo.eu";
        var confirmUrl = $"{siteUrl}/newsletter/potwierdz?token={token}";
        var html = EmailService.BuildHtml(
            title: "Potwierdź zapis na newsletter",
            mainText: "Kliknij poniższy przycisk, aby potwierdzić zapis na newsletter CARIZO. Link jest ważny przez <strong style=\"color:#fff\">24 godziny</strong>.",
            detailsHtml: "<p style=\"font-size:12px;color:#666;margin:0\">Jeśli nie zapisywałeś/aś się na newsletter CARIZO, zignoruj tę wiadomość.</p>",
            ctaUrl: confirmUrl,
            ctaLabel: "Potwierdź zapis"
        );

        // Fire-and-forget — don't block the HTTP response waiting for SMTP
        _ = _email.SendAsync(email, "Potwierdź zapis na newsletter — CARIZO", html)
            .ContinueWith(t => { if (t.IsFaulted) _logger.LogError(t.Exception, "Newsletter email failed for {Email}", email); },
                TaskContinuationOptions.OnlyOnFaulted);

        return Ok(new { message = "Sprawdź swoją skrzynkę email i kliknij link potwierdzający." });
    }

    [HttpGet("confirm")]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> Confirm([FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { message = "Brakuje tokenu." });

        var record = await _context.NewsletterSubscribers
            .FirstOrDefaultAsync(n => n.ConfirmationToken == token);

        if (record == null || record.ConfirmationTokenExpires < DateTime.UtcNow)
            return BadRequest(new { message = "Link potwierdzający wygasł lub jest nieprawidłowy." });

        record.IsConfirmed = true;
        record.IsActive = true;
        record.ConfirmedAt = DateTime.UtcNow;
        record.ConfirmationToken = null;
        record.ConfirmationTokenExpires = null;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Zapis potwierdzony! Możesz teraz korzystać z newslettera CARIZO." });
    }

    [HttpPost("unsubscribe")]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> Unsubscribe([FromBody] NewsletterSubscribeDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest(new { message = "Podaj adres email." });

        var email = dto.Email.Trim().ToLowerInvariant();
        var record = await _context.NewsletterSubscribers.FirstOrDefaultAsync(n => n.Email == email && n.IsActive);
        if (record == null) return NotFound(new { message = "Adres email nie jest zapisany na newsletter." });

        record.IsActive = false;
        record.UnsubscribedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { message = "Wypisano z newslettera." });
    }
}

public record NewsletterSubscribeDto([Required] string Email);
