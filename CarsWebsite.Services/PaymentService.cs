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
        { (ServiceType.EventFeatured, 7),  (9.99m,  "Wyróżnienie wydarzenia – 7 dni") },
        { (ServiceType.EventFeatured, 14), (17.99m, "Wyróżnienie wydarzenia – 14 dni") },
        { (ServiceType.EventFeatured, 30), (29.99m, "Wyróżnienie wydarzenia – 30 dni") },
    };

    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly INotificationService _notifications;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        AppDbContext context,
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        INotificationService notifications,
        ILogger<PaymentService> logger)
    {
        _context = context;
        _config = config;
        _httpClientFactory = httpClientFactory;
        _notifications = notifications;
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

        if (dto.AdvertId.HasValue)
        {
            var advert = await _context.CarAdverts.FindAsync(dto.AdvertId.Value)
                ?? throw new KeyNotFoundException("Ogłoszenie nie istnieje.");
            if (advert.UserId != userId)
                throw new UnauthorizedAccessException("Nie masz dostępu do tego ogłoszenia.");
        }

        var priceInfo = await GetServicePriceAsync(dto.ServiceType, dto.DurationDays);

        var guidPart = Guid.NewGuid().ToString("N")[..8];
        var orderId = $"CARIZO-{userId}-{DateTime.UtcNow:yyyyMMddHHmmss}-{guidPart}";
        if (orderId.Length > 40) orderId = orderId[..40];

        var payment = new Payment
        {
            UserId = userId,
            AdvertId = dto.AdvertId,
            EventId = dto.EventId,
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

    public async Task HandleWebhookAsync(ImojeWebhookDto dto, string rawBody, string signature, string? internalSecret = null)
    {
        var configuredInternalSecret = _config["InternalServiceSecret"]
            ?? Environment.GetEnvironmentVariable("INTERNAL_SERVICE_SECRET")
            ?? "";
        bool isInternalCall = !string.IsNullOrEmpty(configuredInternalSecret)
            && configuredInternalSecret == internalSecret;

        _logger.LogInformation(
            "[Webhook] orderId={OrderId} status={Status} isInternalCall={IsInternal} hasInternalSecret={HasSecret}",
            dto.OrderId, dto.Status, isInternalCall, !string.IsNullOrEmpty(configuredInternalSecret));

        if (!isInternalCall && !VerifySignature(rawBody, signature))
        {
            _logger.LogWarning(
                "[Webhook] Odrzucono - brak dopasowania podpisu lub sekretu wewnętrznego. orderId={OrderId} sigLen={SigLen} secretConfigured={SecretConfigured}",
                dto.OrderId, signature?.Length ?? 0, !string.IsNullOrEmpty(configuredInternalSecret));
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

        if (dto.Status is "settled" or "confirmed" or "authorized" or "completed" or "Completed" or "success" or "paid")
        {
            payment.Status = PaymentStatus.Completed;
            payment.PaidAt = DateTime.UtcNow;
            payment.ImojeTransactionId = dto.TransactionId;
            await _context.SaveChangesAsync();

            _ = _notifications.NotifyAsync(payment.UserId, EmailNotificationType.PaymentConfirmed,
                "Płatność potwierdzona",
                $"Twoja płatność za usługę \"{payment.ServiceDescription}\" w kwocie {payment.Amount:0.00} PLN została pomyślnie zrealizowana.",
                advertId: payment.AdvertId, paymentId: payment.Id);

            await ActivateServiceAsync(payment);
        }
        else if (dto.Status is "rejected" or "cancelled" or "error" or "Failed" or "Cancelled" or "Refunded")
        {
            payment.Status = PaymentStatus.Failed;
            await _context.SaveChangesAsync();

            _ = _notifications.NotifyAsync(payment.UserId, EmailNotificationType.PaymentFailed,
                "Płatność nieudana",
                $"Niestety Twoja płatność za usługę \"{payment.ServiceDescription}\" nie została zrealizowana. Możesz spróbować ponownie.",
                advertId: payment.AdvertId, paymentId: payment.Id);
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
        // IMOJE_MERCHANT_ID = "Identyfikator klienta" (used in URL path)
        // IMOJE_API_KEY     = "Token autoryzacyjny" from Klucze API (Bearer auth)
        // IMOJE_SERVICE_ID  = "Identyfikator sklepu" (sent in body as serviceId)
        var merchantId = Environment.GetEnvironmentVariable("IMOJE_MERCHANT_ID") ?? "";
        var apiKey     = Environment.GetEnvironmentVariable("IMOJE_API_KEY") ?? "";
        var serviceId  = Environment.GetEnvironmentVariable("IMOJE_SERVICE_ID") is { Length: > 0 } sid ? sid : merchantId;
        var apiBase    = Environment.GetEnvironmentVariable("IMOJE_API_URL") ?? "https://api.imoje.pl/v1/merchant";
        var siteUrl    = Environment.GetEnvironmentVariable("IMOJE_SITE_URL") ?? "https://carizo.pl";

        _logger.LogInformation(
            "[Imoje] Config: MerchantId={HasMid} (len={MidLen}), ApiKey={HasKey} (len={KeyLen}, pfx={KeyPfx}), ServiceId={HasSid} (len={SidLen}), ApiBase={ApiBase}",
            string.IsNullOrEmpty(merchantId) ? "EMPTY" : "SET", merchantId.Length,
            string.IsNullOrEmpty(apiKey)     ? "EMPTY" : "SET", apiKey.Length,
            apiKey.Length >= 6 ? apiKey[..6] + "..." : "(short)",
            string.IsNullOrEmpty(serviceId)  ? "EMPTY" : "SET", serviceId.Length,
            apiBase);

        if (string.IsNullOrEmpty(merchantId) || string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Bramka imoje nie jest skonfigurowana (brak merchantId/apiKey). Zwracam URL zastępczy.");
            return $"{siteUrl}/payment/return?status=pending&paymentId={payment.Id}";
        }

        var body = new
        {
            type = "payment",
            serviceId,
            amount   = (int)(payment.Amount * 100),
            currency = "PLN",
            orderId,
            title    = payment.ServiceDescription,
            successReturnUrl  = $"{siteUrl}/payment/return?status=success&paymentId={payment.Id}&advertId={payment.AdvertId}",
            failureReturnUrl  = $"{siteUrl}/payment/return?status=failure&paymentId={payment.Id}",
            notificationUrl   = $"{siteUrl}/api/payment/webhook",
            customer = new
            {
                firstName = user.Name,
                lastName  = user.Surname,
                email     = user.Email
            }
        };

        var serializedBody = JsonSerializer.Serialize(body);
        _logger.LogInformation("[Imoje] Request body (preview): {Body}", serializedBody[..Math.Min(200, serializedBody.Length)]);

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);


        var requestUrl = $"{apiBase.TrimEnd('/')}/{merchantId}/transaction";
        _logger.LogInformation("[Imoje] Wysyłam request: POST {Url}", requestUrl);

        HttpResponseMessage response;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = new StringContent(serializedBody, Encoding.UTF8, "application/json")
            };
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        }
        catch (Exception ex)
        {
            _logger.LogError("[Imoje] Błąd sieci przy wywołaniu {Url}: {ExType} – {Message}",
                requestUrl, ex.GetType().Name, ex.Message);
            throw new InvalidOperationException($"Błąd sieci bramki płatności: {ex.Message}");
        }

        _logger.LogInformation("[Imoje] Status odpowiedzi: {StatusCode}", (int)response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            string err = "";
            try { err = await response.Content.ReadAsStringAsync(); } catch { }
            _logger.LogError(
                "[Imoje] Błąd API: status={StatusCode}, url={Url}, body={Body}",
                (int)response.StatusCode, requestUrl, err);
            throw new InvalidOperationException($"Błąd bramki płatności ({(int)response.StatusCode}). Spróbuj ponownie.");
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
        if (payment.ServiceType == ServiceType.EventFeatured)
        {
            await ActivateEventServiceAsync(payment);
            return;
        }

        if (payment.AdvertId == null) return;

        _logger.LogInformation(
            "Aktywacja usługi {ServiceType} dla ogłoszenia {AdvertId}, {Days} dni, płatność #{PaymentId}",
            payment.ServiceType, payment.AdvertId, payment.DurationDays, payment.Id);

        var advert = await _context.CarAdverts
            .FirstOrDefaultAsync(a => a.Id == payment.AdvertId);

        if (advert != null)
        {
            if (payment.ServiceType == ServiceType.Refresh)
            {
                advert.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                var badge = payment.ServiceType switch
                {
                    ServiceType.Top      => "TOP",
                    ServiceType.Premium  => "PREMIUM",
                    ServiceType.Featured => "FEATURED",
                    _                    => null
                };

                if (badge != null)
                {
                    var baseDate = advert.BadgeExpiresAt.HasValue && advert.BadgeExpiresAt > DateTime.UtcNow
                        ? advert.BadgeExpiresAt.Value
                        : DateTime.UtcNow;
                    advert.Badge = badge;
                    advert.BadgeExpiresAt = baseDate.AddDays(payment.DurationDays ?? 30);
                }
            }

            await _context.SaveChangesAsync();
        }
        else
        {
            _logger.LogWarning("ActivateServiceAsync: ogłoszenie {AdvertId} nie znalezione.", payment.AdvertId);
        }

        var notifType = payment.ServiceType switch
        {
            ServiceType.Top      => EmailNotificationType.TopStarted,
            ServiceType.Premium  => EmailNotificationType.PremiumStarted,
            ServiceType.Featured => EmailNotificationType.FeaturedStarted,
            ServiceType.Refresh  => EmailNotificationType.RefreshStarted,
            _                    => EmailNotificationType.PromotionActivated
        };

        var typeName = payment.ServiceType switch
        {
            ServiceType.Top      => "Wyróżnienie TOP",
            ServiceType.Premium  => "Oferta Premium",
            ServiceType.Featured => "Wyróżnienie",
            ServiceType.Refresh  => "Odświeżenie",
            _                    => "Promocja"
        };

        _ = _notifications.NotifyAsync(payment.UserId, notifType,
            $"{typeName} aktywowane",
            $"Usługa \"{payment.ServiceDescription}\" została aktywowana na {payment.DurationDays} dni.",
            advertId: payment.AdvertId, paymentId: payment.Id);
    }

    private async Task ActivateEventServiceAsync(Payment payment)
    {
        if (payment.EventId == null) return;

        _logger.LogInformation(
            "Aktywacja wyróżnienia wydarzenia {EventId}, {Days} dni, płatność #{PaymentId}",
            payment.EventId, payment.DurationDays, payment.Id);

        var ev = await _context.Events.FirstOrDefaultAsync(e => e.Id == payment.EventId);
        if (ev != null)
        {
            var baseDate = ev.FeaturedUntil.HasValue && ev.FeaturedUntil > DateTime.UtcNow
                ? ev.FeaturedUntil.Value
                : DateTime.UtcNow;
            ev.IsFeatured = true;
            ev.FeaturedUntil = baseDate.AddDays(payment.DurationDays ?? 7);
            await _context.SaveChangesAsync();
        }
        else
        {
            _logger.LogWarning("ActivateEventServiceAsync: wydarzenie {EventId} nie znalezione.", payment.EventId);
        }

        _ = _notifications.NotifyAsync(payment.UserId, EmailNotificationType.PromotionActivated,
            "Wyróżnienie wydarzenia aktywowane",
            $"Twoje wydarzenie zostało wyróżnione na {payment.DurationDays} dni.",
            paymentId: payment.Id);
    }

    private bool VerifySignature(string rawBody, string signature)
    {
        var secret = Environment.GetEnvironmentVariable("IMOJE_WEBHOOK_SECRET")
            ?? _config["Imoje:WebhookSecret"]
            ?? "";
        if (string.IsNullOrEmpty(secret)) return false;

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
        EventId = p.EventId,
        DurationDays = p.DurationDays
    };
}
