using CarsWebsite;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
public class NewsletterController : ControllerBase
{
    private readonly AppDbContext _context;

    public NewsletterController(AppDbContext context)
    {
        _context = context;
    }

    public class SubscribeRequest
    {
        public string Email { get; set; } = string.Empty;
    }

    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest request)
    {
        var email = request?.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return BadRequest(new { message = "Podaj prawidłowy adres e-mail." });

        var existing = await _context.NewsletterSubscribers
            .FirstOrDefaultAsync(n => n.Email == email);

        if (existing != null)
        {
            if (existing.IsActive)
                return Ok(new { message = "Ten adres e-mail jest już zapisany do newslettera." });

            // Re-subscribe
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
}
