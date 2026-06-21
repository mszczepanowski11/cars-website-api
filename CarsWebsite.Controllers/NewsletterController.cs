using CarsWebsite;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NewsletterController : ControllerBase
{
    private readonly AppDbContext _context;

    public NewsletterController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] NewsletterSubscribeDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || !new EmailAddressAttribute().IsValid(dto.Email))
            return BadRequest(new { message = "Podaj prawidłowy adres e-mail." });

        var email = dto.Email.Trim().ToLowerInvariant();
        var existing = await _context.NewsletterSubscribers.FirstOrDefaultAsync(n => n.Email == email);

        if (existing != null)
        {
            if (existing.IsActive)
                return Ok(new { message = "Ten adres e-mail jest już zapisany do newslettera." });

            existing.IsActive = true;
            existing.UnsubscribedAt = null;
            existing.SubscribedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok(new { message = "Zapisano do newslettera." });
        }

        _context.NewsletterSubscribers.Add(new NewsletterSubscriber
        {
            Email = email,
            IsActive = true,
            SubscribedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        return Ok(new { message = "Zapisano do newslettera." });
    }

    [HttpPost("unsubscribe")]
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
