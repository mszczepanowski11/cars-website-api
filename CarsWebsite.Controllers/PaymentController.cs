using cars_website_api.CarsWebsite.DTOs.Payment;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

[ApiController]
[Route("api/[controller]")]
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
    [HttpPost("initiate")]
    public async Task<IActionResult> Initiate([FromBody] InitiatePaymentDto dto)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId)) return Unauthorized();

        try { return Ok(await _paymentService.InitiatePaymentAsync(dto, userId)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return StatusCode(502, new { message = ex.Message }); }
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

    /// <summary>
    /// Webhook imoje – wywoływany automatycznie po zaksięgowaniu lub odrzuceniu płatności.
    /// </summary>
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var rawBody = await reader.ReadToEndAsync();
        var signature = Request.Headers["X-Imoje-Signature"].FirstOrDefault() ?? string.Empty;
        var internalSecret = Request.Headers["X-Internal-Secret"].FirstOrDefault();

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
            await _paymentService.HandleWebhookAsync(dto, rawBody, signature, internalSecret);
            return Ok();
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpGet("admin/all")]
    public async Task<IActionResult> AdminGetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
        => Ok(await _paymentService.GetAllPaymentsAsync(page, pageSize));
}
