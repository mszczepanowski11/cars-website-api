using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class InvoiceController : ControllerBase
{
    private readonly IInvoiceService _invoiceService;
    private readonly AppDbContext _context;

    public InvoiceController(IInvoiceService invoiceService, AppDbContext context)
    {
        _invoiceService = invoiceService;
        _context = context;
    }

    private async Task<bool> IsAdminAsync()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var uid)) return false;
        var user = await _context.Users.FindAsync(uid);
        return user?.IsAdmin == true;
    }

    private int GetUserId()
    {
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid);
        return uid;
    }

    /// <summary>Lista faktur zalogowanego użytkownika.</summary>
    [HttpGet("my")]
    public async Task<IActionResult> GetMine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        return Ok(await _invoiceService.GetUserInvoicesAsync(GetUserId(), page, pageSize));
    }

    /// <summary>Szczegóły faktury (właściciel lub admin).</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var isAdmin = await IsAdminAsync();
            return Ok(await _invoiceService.GetInvoiceAsync(id, isAdmin ? 0 : GetUserId()));
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    /// <summary>Pobierz fakturę jako plik HTML (do wydruku / zapisu PDF).</summary>
    [HttpGet("{id:int}/html")]
    public async Task<IActionResult> DownloadHtml(int id)
    {
        try
        {
            var isAdmin = await IsAdminAsync();
            var bytes = await _invoiceService.GenerateInvoiceHtmlAsync(id, isAdmin ? 0 : GetUserId());
            return File(bytes, "text/html; charset=utf-8", $"faktura-CARIZO-{id}.html");
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    /// <summary>Admin: wszystkie faktury w systemie.</summary>
    [HttpGet("admin/all")]
    public async Task<IActionResult> AdminGetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (!await IsAdminAsync()) return Forbid();
        return Ok(await _invoiceService.GetAllInvoicesAsync(page, pageSize));
    }

    /// <summary>Admin: ręczne wygenerowanie faktur za wskazany miesiąc/rok.</summary>
    [HttpPost("admin/generate")]
    public async Task<IActionResult> AdminGenerate(
        [FromQuery] int month,
        [FromQuery] int year)
    {
        if (!await IsAdminAsync()) return Forbid();
        await _invoiceService.GenerateMonthlyInvoicesAsync(month, year);
        return Ok(new { message = $"Faktury za {month:D2}/{year} zostały wygenerowane." });
    }

    /// <summary>Admin: wyślij fakturę ponownie e-mailem.</summary>
    [HttpPost("admin/{id:int}/send")]
    public async Task<IActionResult> AdminSend(int id)
    {
        if (!await IsAdminAsync()) return Forbid();
        try
        {
            await _invoiceService.SendInvoiceByEmailAsync(id);
            return Ok(new { message = "Faktura została wysłana." });
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}
