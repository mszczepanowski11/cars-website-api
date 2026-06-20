using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class InvoiceController : ControllerBase
{
    private readonly IInvoiceService _invoiceService;

    public InvoiceController(IInvoiceService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    private bool IsAdmin() => User.FindFirstValue("isAdmin") == "true";

    private int GetUserId()
    {
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid);
        return uid;
    }

    [HttpGet("my")]
    public async Task<IActionResult> GetMine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        return Ok(await _invoiceService.GetUserInvoicesAsync(GetUserId(), page, pageSize));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try { return Ok(await _invoiceService.GetInvoiceAsync(id, userId, IsAdmin())); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpGet("{id:int}/html")]
    public async Task<IActionResult> DownloadHtml(int id)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        try
        {
            var bytes = await _invoiceService.GenerateInvoiceHtmlAsync(id, userId, IsAdmin());
            return File(bytes, "text/html; charset=utf-8", $"faktura-CARIZO-{id}.html");
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpGet("admin/all")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AdminGetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
        => Ok(await _invoiceService.GetAllInvoicesAsync(page, pageSize));

    [HttpPost("admin/generate")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AdminGenerate(
        [FromQuery] int month,
        [FromQuery] int year)
    {
        await _invoiceService.GenerateMonthlyInvoicesAsync(month, year);
        return Ok(new { message = $"Faktury za {month:D2}/{year} zostały wygenerowane." });
    }

    [HttpPost("admin/{id:int}/send")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AdminSend(int id)
    {
        try
        {
            await _invoiceService.SendInvoiceByEmailAsync(id);
            return Ok(new { message = "Faktura została wysłana." });
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost("admin/{id:int}/resend")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AdminResend(int id)
    {
        try
        {
            await _invoiceService.SendInvoiceByEmailAsync(id);
            return Ok(new { message = "Faktura została ponownie wysłana." });
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}