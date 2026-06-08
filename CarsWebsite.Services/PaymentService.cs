using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CarsWebsite;
using cars_website_api.CarsWebsite.DTOs.Payment;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.EntityFrameworkCore;

public class PaymentService : IPaymentService
{
    private static readonly Dictionary<(ServiceType, int), (decimal Price, string Description)> PriceTable = new()
    {
        { (ServiceType.Top, 7),      (19.99m, "Wyróżnienie TOP – 7 dni") },
        { (ServiceType.Top, 14),     (29.99m, "Wyróżnienie TOP – 14 dni") },
        { (ServiceType.Top, 30),     (49.99m, "Wyróżnienie TOP – 30 dni") },
        { (ServiceType.Premium, 7),  (29.99m, "Oferta Premium – 7 dni") },
        { (ServiceType.Premium, 14), (44.99m, "Oferta Premium – 14 dni") },
        { (ServiceType.Premium, 30), (79.99m, "Oferta Premium – 30 dni") },
        { (ServiceType.Featured, 7),  (14.99m, "Wyróżnienie – 7 dni") },
        { (ServiceType.Featured, 14), (24.99m, "Wyróżnienie – 14 dni") },
        { (ServiceType.Featured, 30), (39.99m, "Wyróżnienie – 30 dni") },
        { (ServiceType.Refresh, 1),   (4.99m,  "Odświeżenie ogłoszenia") },
    };

    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        AppDbContext context,
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ILogger<PaymentService> logger)
    {
        _context = context;
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task<ServicePriceDto> GetServicePriceAsync(ServiceType serviceType, int durationDays)
    {
        if (!PriceTable.TryGetValue((serviceType, durationDays), out var entry))
            throw new ArgumentException($"Brak cennika dla {serviceType} / {durationDays} dni.");

        return Task.FromResult(new ServicePriceDto
        {
            ServiceType = serviceType,
            DurationDays = durationDays,
            Price = entry.Price,
            Description = entry.Description
        });
    }

    public async Task<PaymentInitiatedDto> InitiatePaymentAsync(InitiatePaymentDto dto, int userId)
    {
        var user = await _context.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("Użytkownik nie istnieje.");

        var priceInfo = await GetServicePriceAsync(dto.ServiceType, dto.DurationDays);

        var guidPart = Guid.NewGuid().ToString("N")[..8];
        var orderId = $"CARIZO-{userId}-{DateTime.UtcNow:yyyyMMddHHmmss}-{guidPart}";
        if (orderId.Length > 40) orderId = orderId[..40];

        var payment = new Payment
        {
            UserId = userId,
            AdvertId = dto.AdvertId,
            ServiceType = dto.ServiceType,
            ServiceDescription = priceInfo.Description,
            Amount = priceInfo.Price,
            Currency = "PLN",
            Status = PaymentStatus.Pending,
            ImojeOrderId = orderId,
            DurationDays = dto.DurationDays,
            CreatedAt = DateTime.UtcNow
        };

        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();

        var paymentUrl = await CreateImojeTransactionAsync(payment, user, orderId);

        return new PaymentInitiatedDto
        {
            PaymentId = payment.Id,
            PaymentUrl = paymentUrl,
            Amount = priceInfo.Price,
            OrderId = orderId
        };
    }

    public async Task HandleWebhookAsync(ImojeWebhookDto dto, string rawBody, string signature)
    {
        if (!VerifySignature(rawBody, signature))
        {
            _logger.LogWarning("Nieprawidłowy podpis webhooka imoje dla zamówienia {OrderId}", dto.OrderId);
            throw new UnauthorizedAccessException("Nieprawidłowy podpis webhooka.");
        }

        var payment = await _context.Payments
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.ImojeOrderId == dto.OrderId);

        if (payment == null)
        {
            _logger.LogWarning("Nie znaleziono płatności dla orderId={OrderId}", dto.OrderId);
            return;
        }

        if (payment.Status == PaymentStatus.Completed) return;

        if (dto.Status is "settled" or "confirmed" or "authorized")
        {
            payment.Status = PaymentStatus.Completed;
            payment.PaidAt = DateTime.UtcNow;
            payment.ImojeTransactionId = dto.TransactionId;
            await _context.SaveChangesAsync();
            await ActivateServiceAsync(payment);
        }
        else if (dto.Status is "rejected" or "cancelled" or "error")
        {
            payment.Status = PaymentStatus.Failed;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<PagedResult<PaymentResponseDto>> GetUserPaymentsAsync(int userId, int page, int pageSize)
    {
        var query = _context.Payments
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt);

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<PaymentResponseDto>
        {
            Items = items.Select(MapToDto).ToList(),
            TotalCount = total
        };
    }

    public async Task<PagedResult<PaymentResponseDto>> GetAllPaymentsAsync(int page, int pageSize)
    {
        var query = _context.Payments
            .Include(p => p.User)
            .OrderByDescending(p => p.CreatedAt);

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<PaymentResponseDto>
        {
            Items = items.Select(MapToDto).ToList(),
            TotalCount = total
        };
    }

    private async Task<string> CreateImojeTransactionAsync(Payment payment, User user, string orderId)
    {
        var section = _config.GetSection("Imoje");
        var serviceId = section["ServiceId"];
        var apiKey = section["ApiKey"];
        var apiUrl = section["ApiUrl"] ?? "https://sandbox.imoje.pl";
        var siteUrl = section["SiteUrl"] ?? "https://carizo.pl";

        if (string.IsNullOrEmpty(serviceId) || string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Bramka imoje nie jest skonfigurowana. Zwracam URL zastępczy.");
            return $"{siteUrl}/platnosc/oczekujaca?paymentId={payment.Id}";
        }

        var body = new
        {
            serviceId,
            amount = (int)(payment.Amount * 100),
            currency = "PLN",
            orderId,
            customerFirstName = user.Name,
            customerLastName = user.Surname,
            customerEmail = user.Email,
            urlSuccess = $"{siteUrl}/platnosc/sukces?paymentId={payment.Id}",
            urlFailure = $"{siteUrl}/platnosc/blad?paymentId={payment.Id}",
            urlReturn = $"{siteUrl}/platnosc/powrot",
            urlNotification = $"{siteUrl}/api/Payment/webhook",
            description = payment.ServiceDescription
        };

        var client = _httpClientFactory.CreateClient("imoje");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ServiceKey", apiKey);

        var response = await client.PostAsync(
            $"{apiUrl}/payment/v1/transaction",
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        );

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("Błąd API imoje: {Error}", err);
            throw new InvalidOperationException("Błąd bramki płatności. Spróbuj ponownie.");
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement
            .GetProperty("action")
            .GetProperty("url")
            .GetString()
            ?? throw new InvalidOperationException("Brak URL płatności w odpowiedzi imoje.");
    }

    private async Task ActivateServiceAsync(Payment payment)
    {
        if (payment.AdvertId == null) return;

        // TODO: Wywołać PromotionService gdy zostanie zaimplementowany,
        // przekazując payment.ServiceType i payment.DurationDays.
        _logger.LogInformation(
            "Aktywacja usługi {ServiceType} dla ogłoszenia {AdvertId}, {Days} dni, płatność #{PaymentId}",
            payment.ServiceType, payment.AdvertId, payment.DurationDays, payment.Id);

        await Task.CompletedTask;
    }

    private bool VerifySignature(string rawBody, string signature)
    {
        var secret = _config["Imoje:WebhookSecret"];
        if (string.IsNullOrEmpty(secret)) return true;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
        var expected = Convert.ToHexString(hash).ToLowerInvariant();
        return expected.Equals(signature?.ToLowerInvariant(), StringComparison.Ordinal);
    }

    private static PaymentResponseDto MapToDto(Payment p) => new()
    {
        Id = p.Id,
        ServiceType = p.ServiceType,
        ServiceDescription = p.ServiceDescription,
        Amount = p.Amount,
        Currency = p.Currency,
        Status = p.Status,
        ImojeTransactionId = p.ImojeTransactionId,
        CreatedAt = p.CreatedAt,
        PaidAt = p.PaidAt,
        AdvertId = p.AdvertId,
        DurationDays = p.DurationDays
    };
}
