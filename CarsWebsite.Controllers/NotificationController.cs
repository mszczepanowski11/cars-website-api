using cars_website_api.CarsWebsite.DTOs.Notification;
using CarsWebsite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("global")]
public class NotificationController : ControllerBase
{
    private readonly AppDbContext _context;

    private static readonly List<(string Category, string Label)> CategoryLabels =
    [
        ("Registration",    "Konto i bezpieczeństwo"),
        ("Adverts",         "Ogłoszenia"),
        ("AdvertExpiry",    "Wygasanie ogłoszeń"),
        ("Promotions",      "Promocje"),
        ("PromotionExpiry", "Wygasanie promocji"),
        ("Payments",        "Płatności"),
        ("Invoices",        "Faktury"),
        ("Messages",        "Wiadomości"),
    ];

    public NotificationController(AppDbContext context) => _context = context;

    private int UserId
    {
        get
        {
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid);
            return uid;
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var uid = UserId;
        var query = _context.AppNotifications.Where(n => n.UserId == uid).OrderByDescending(n => n.CreatedAt);
        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new PagedResult<NotificationResponseDto>
        {
            Items = items.Select(Map).ToList(),
            TotalCount = total
        });
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        var count = await _context.AppNotifications.CountAsync(n => n.UserId == UserId && !n.IsRead);
        return Ok(new { count });
    }

    [HttpPut("{id:int}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var n = await _context.AppNotifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == UserId);
        if (n == null) return NotFound();
        n.IsRead = true;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var uid = UserId;
        var unread = await _context.AppNotifications.Where(n => n.UserId == uid && !n.IsRead).ToListAsync();
        unread.ForEach(n => n.IsRead = true);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var n = await _context.AppNotifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == UserId);
        if (n == null) return NotFound();
        _context.AppNotifications.Remove(n);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences()
    {
        var uid = UserId;
        var settings = await _context.UserNotificationSettings.Where(s => s.UserId == uid).ToListAsync();
        var result = CategoryLabels.Select(cl => new NotificationPreferenceDto
        {
            Category = cl.Category,
            Label = cl.Label,
            EmailEnabled = settings.FirstOrDefault(s => s.Category == cl.Category)?.EmailEnabled ?? true
        }).ToList();
        return Ok(result);
    }

    [HttpPut("preferences")]
    public async Task<IActionResult> UpdatePreference([FromBody] UpdateNotificationPreferenceDto dto)
    {
        var uid = UserId;
        var setting = await _context.UserNotificationSettings
            .FirstOrDefaultAsync(s => s.UserId == uid && s.Category == dto.Category);
        if (setting == null)
        {
            _context.UserNotificationSettings.Add(new UserNotificationSetting
            {
                UserId = uid, Category = dto.Category, EmailEnabled = dto.EmailEnabled
            });
        }
        else
        {
            setting.EmailEnabled = dto.EmailEnabled;
        }
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private static NotificationResponseDto Map(AppNotification n) => new()
    {
        Id = n.Id, Type = n.Type, Title = n.Title, Content = n.Content,
        IsRead = n.IsRead, CreatedAt = n.CreatedAt,
        AdvertId = n.AdvertId, PaymentId = n.PaymentId, InvoiceId = n.InvoiceId
    };
}
