using CarsWebsite;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.Controllers;

[Route("api/[controller]")]
[ApiController]
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
            return BadRequest(new { message = "Nieprawidłowy adres email." });

        var email = dto.Email.Trim().ToLowerInvariant();
        var existing = await _context.NewsletterSubscribers.FirstOrDefaultAsync(n => n.Email == email);

        if (existing != null)
        {
            if (existing.IsActive)
                return Conflict(new { message = "Ten adres email jest już zapisany na newsletter." });

            existing.IsActive = true;
            existing.SubscribedAt = DateTime.UtcNow;
            existing.UnsubscribedAt = null;
        }
        else
        {
            _context.NewsletterSubscribers.Add(new NewsletterSubscriber
            {
                Email = email,
                IsActive = true,
                SubscribedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
        return Ok(new { message = "Zapisano na newsletter. Dziękujemy!" });
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
