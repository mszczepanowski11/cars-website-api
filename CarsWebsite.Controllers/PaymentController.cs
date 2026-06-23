using cars_website_api.CarsWebsite.DTOs.Payment;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("global")]
public class PaymentController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public PaymentController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    /// <summary>Pobierz cenę dla wybranej usługi i czasu trwania.</summary>
    [HttpGet("price")]
    public async Task<IActionResult> GetPrice(
        [FromQuery] ServiceType serviceType,
        [FromQuery] int durationDays)
    {
        try { return Ok(await _paymentService.GetServicePriceAsync(serviceType, durationDays)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Inicjuje transakcję w bramce imoje i zwraca URL płatności.</summary>
    [Authorize]
    [EnableRateLimiting("auth")]
    [HttpPost("initiate")]
    public async Task<IActionResult> Initiate([FromBody] InitiatePaymentDto dto)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId)) return Unauthorized();

        try { return Ok(await _paymentService.InitiatePaymentAsync(dto, userId)); }
        catch (ArgumentException ex)          { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex)       { return NotFound(new { message = ex.Message }); }
        catch (UnauthorizedAccessException ex){ return StatusCode(403, new { message = ex.Message }); }
        catch (InvalidOperationException ex)  { return StatusCode(502, new { message = ex.Message }); }
        catch (Exception ex)                  { return StatusCode(500, new { message = $"Błąd bramki płatności: {ex.Message}" }); }
    }

    /// <summary>Lista płatności zalogowanego użytkownika.</summary>
    [Authorize]
    [HttpGet("my")]
    public async Task<IActionResult> GetMine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId)) return Unauthorized();
        return Ok(await _paymentService.GetUserPaymentsAsync(userId, page, pageSize));
    }

    /// <summary>Weryfikacja URL webhooka przez imoje (GET probe).</summary>
    [HttpGet("webhook")]
    public IActionResult WebhookProbe() => Ok();

    /// <summary>
    /// Webhook imoje – wywoływany automatycznie po zaksięgowaniu lub odrzuceniu płatności.
    /// </summary>
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var rawBody = await reader.ReadToEndAsync();
        var signature = Request.Headers["X-Imoje-Signature"].FirstOrDefault() ?? string.Empty;

        ImojeWebhookDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ImojeWebhookDto>(rawBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return BadRequest("Nieprawidłowy payload."); }

        if (dto == null) return BadRequest("Pusty payload.");

        try
        {
            await _paymentService.HandleWebhookAsync(dto, rawBody, signature);
            return Ok();
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (InvalidOperationException ex) when (ex.Message == "Amount mismatch") { return BadRequest(new { message = "Amount mismatch" }); }
    }

    /// <summary>Diagnostyka konfiguracji imoje (bez ujawniania kluczy).</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpGet("admin/imoje-config")]
    public IActionResult ImojeConfig([FromServices] IConfiguration config)
    {
        var s = config.GetSection("Imoje");
        return Ok(new
        {
            serviceIdSet     = !string.IsNullOrEmpty(s["ServiceId"]),
            serviceIdLength  = s["ServiceId"]?.Length ?? 0,
            serviceIdPrefix  = s["ServiceId"]?.Length > 8 ? s["ServiceId"]![..8] + "..." : s["ServiceId"],
            serviceKeySet    = !string.IsNullOrEmpty(s["ServiceKey"]),
            serviceKeyLength = s["ServiceKey"]?.Length ?? 0,
            merchantIdSet    = !string.IsNullOrEmpty(s["MerchantId"]),
            merchantIdLength = s["MerchantId"]?.Length ?? 0,
            webhookSecretSet = !string.IsNullOrEmpty(s["WebhookSecret"]),
            sandbox          = string.Equals(s["Environment"], "sandbox", StringComparison.OrdinalIgnoreCase),
            environment      = s["Environment"] ?? "(not set — defaults to production)",
            siteUrl          = s["SiteUrl"] ?? "(not set)",
            apiUrl           = s["ApiUrl"] ?? "(not set)",
        });
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpGet("admin/all")]
    public async Task<IActionResult> AdminGetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
        => Ok(await _paymentService.GetAllPaymentsAsync(page, pageSize));

    [Authorize(Policy = "AdminOnly")]
    [HttpPatch("admin/{id:int}/status")]
    public async Task<IActionResult> AdminUpdateStatus(int id, [FromBody] AdminUpdatePaymentStatusDto dto)
    {
        var payment = await _paymentService.AdminUpdateStatusAsync(id, dto.Status);
        if (payment == null) return NotFound();
        return Ok(payment);
    }
}
