using System.Security.Cryptography;
using System.Text;
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
    private readonly INotificationService _notifications;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        AppDbContext context,
        IConfiguration config,
        INotificationService notifications,
        ILogger<PaymentService> logger)
    {
        _context = context;
        _config = config;
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
        _logger.LogInformation(
            "[Payment/Initiate] userId={UserId} serviceType={ServiceType} durationDays={Days} advertId={AdvertId} eventId={EventId}",
            userId, dto.ServiceType, dto.DurationDays, dto.AdvertId, dto.EventId);

        var user = await _context.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("Użytkownik nie istnieje.");

        if (dto.AdvertId.HasValue)
        {
            var advert = await _context.Adverts.FirstOrDefaultAsync(a => a.Id == dto.AdvertId.Value);
            if (advert == null)
            {
                _logger.LogWarning("[Payment/Initiate] advertId={AdvertId} not found in Adverts", dto.AdvertId.Value);
                throw new KeyNotFoundException("Ogłoszenie nie istnieje.");
            }
            if (advert.UserId != userId)
                throw new UnauthorizedAccessException("Nie masz dostępu do tego ogłoszenia.");
        }

        var priceInfo = await GetServicePriceAsync(dto.ServiceType, dto.DurationDays);
        _logger.LogInformation("[Payment/Initiate] price={Price} desc={Desc}", priceInfo.Price, priceInfo.Description);

        // K-1: Verify ownership before creating payment
        if (dto.AdvertId.HasValue)
        {
            var ownerCheck = await _context.CarAdverts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == dto.AdvertId.Value);
            if (ownerCheck == null || ownerCheck.UserId != userId)
                throw new UnauthorizedAccessException("Nie jesteś właścicielem tego ogłoszenia.");
        }
        if (dto.EventId.HasValue)
        {
            var eventCheck = await _context.Events.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == dto.EventId.Value);
            if (eventCheck == null || eventCheck.CreatedByUserId != userId)
                throw new UnauthorizedAccessException("Nie jesteś właścicielem tego wydarzenia.");
        }

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
            CreatedAt = DateTime.UtcNow,
            BillingName = dto.BillingName,
            BillingNip = dto.BillingNip,
            BillingStreet = dto.BillingStreet,
            BillingPostalCode = dto.BillingPostalCode,
            BillingCity = dto.BillingCity,
        };

        _logger.LogInformation("[Payment/Initiate] saving Payment record orderId={OrderId}", orderId);
        _context.Payments.Add(payment);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Payment/Initiate] SaveChangesAsync FAILED for Payment: {Message}", ex.Message);
            throw new InvalidOperationException($"Błąd zapisu płatności: {ex.InnerException?.Message ?? ex.Message}");
        }
        _logger.LogInformation("[Payment/Initiate] Payment #{PaymentId} saved", payment.Id);

        var (actionUrl, formFields) = BuildImojeFormData(payment, user, orderId);

        return new PaymentInitiatedDto
        {
            PaymentId  = payment.Id,
            PaymentUrl = actionUrl,
            FormFields = formFields,
            Amount     = priceInfo.Price,
            OrderId    = orderId
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

        // K-2: Lock payment row to prevent duplicate webhook processing
        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            await _context.Database.ExecuteSqlRawAsync(
                "SELECT 1 FROM `Payments` WHERE `ImojeOrderId` = {0} FOR UPDATE", dto.OrderId);

            var payment = await _context.Payments
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.ImojeOrderId == dto.OrderId);

            if (payment == null)
            {
                _logger.LogWarning("Nie znaleziono płatności dla orderId={OrderId}", dto.OrderId);
                await tx.CommitAsync();
                return;
            }

            if (payment.Status == PaymentStatus.Completed)
            {
                await tx.CommitAsync();
                return;
            }

            if (dto.Status is "settled" or "confirmed" or "authorized" or "completed" or "Completed")
            {
                payment.Status = PaymentStatus.Completed;
                payment.PaidAt = DateTime.UtcNow;
                payment.ImojeTransactionId = dto.TransactionId;
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

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
                await tx.CommitAsync();

                _ = _notifications.NotifyAsync(payment.UserId, EmailNotificationType.PaymentFailed,
                    "Płatność nieudana",
                    $"Niestety Twoja płatność za usługę \"{payment.ServiceDescription}\" nie została zrealizowana. Możesz spróbować ponownie.",
                    advertId: payment.AdvertId, paymentId: payment.Id);
            }
            else
            {
                await tx.CommitAsync();
            }
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
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

    // Builds imoje paywall form fields + action URL using the official SDK algorithm.
    // Signature: SHA256(ksort(key=value&...) + serviceKey) + ";sha256"
    private (string ActionUrl, Dictionary<string, string> Fields) BuildImojeFormData(Payment payment, User user, string orderId)
    {
        var section = _config.GetSection("Imoje");
        var serviceId  = section["ServiceId"]  ?? "";
        var serviceKey = section["ServiceKey"] ?? section["ApiKey"] ?? "";
        var merchantId = section["MerchantId"] ?? "";
        var sandbox    = string.Equals(section["Environment"], "sandbox", StringComparison.OrdinalIgnoreCase);
        var siteUrl    = section["SiteUrl"] ?? "https://carizo.pl";

        var actionUrl = sandbox
            ? "https://sandbox.paywall.imoje.pl/payment"
            : "https://paywall.imoje.pl/payment";

        if (string.IsNullOrEmpty(serviceId) || string.IsNullOrEmpty(serviceKey))
        {
            _logger.LogWarning("Bramka imoje nie jest skonfigurowana (brak ServiceId/ServiceKey).");
            return ($"{siteUrl}/payment/return?status=pending&paymentId={payment.Id}", new Dictionary<string, string>());
        }

        var fields = new Dictionary<string, string>
        {
            ["amount"]            = ((int)(payment.Amount * 100)).ToString(),
            ["currency"]          = "PLN",
            ["orderId"]           = orderId,
            ["customerFirstName"] = user.Name ?? "",
            ["customerLastName"]  = user.Surname ?? "",
            ["customerEmail"]     = user.Email ?? "",
            ["urlSuccess"]        = $"{siteUrl}/payment/return?status=success&paymentId={payment.Id}&advertId={payment.AdvertId}",
            ["urlFailure"]        = $"{siteUrl}/payment/return?status=failure&paymentId={payment.Id}",
            ["urlReturn"]         = $"{siteUrl}/payment/return?status=cancel",
            ["urlNotification"]   = $"{siteUrl}/api/payment/webhook",
            ["orderDescription"]  = payment.ServiceDescription,
            ["serviceId"]         = serviceId,
            ["merchantId"]        = merchantId,
        };

        fields["signature"] = ComputeImojeSignature(fields, serviceKey);

        return (actionUrl, fields);
    }

    private static string ComputeImojeSignature(Dictionary<string, string> fields, string serviceKey)
    {
        // Match PHP SDK: ksort() → "key=value&..." → SHA256(data + serviceKey) → append ";sha256"
        var sorted = fields.OrderBy(k => k.Key, StringComparer.Ordinal);
        var data   = string.Join("&", sorted.Select(k => $"{k.Key}={k.Value}"));
        var hash   = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data + serviceKey))).ToLower();
        return $"{hash};sha256";
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
        var secret = _config["Imoje:WebhookSecret"] ?? _config["Imoje:ServiceKey"] ?? _config["Imoje:ApiKey"] ?? "";
        if (string.IsNullOrEmpty(secret)) return false;

        // signature from imoje webhook: "hash;sha256" — same algorithm as payment form
        var parts = signature?.Split(';');
        var hashMethod = parts?.Length == 2 ? parts[1].ToLower() : "sha256";
        var receivedHash = parts?[0]?.ToLower() ?? "";

        if (hashMethod != "sha256") return false;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody + secret))).ToLower();
        return hash == receivedHash;
    }

    public async Task<PaymentResponseDto?> AdminUpdateStatusAsync(int paymentId, string status)
    {
        var payment = await _context.Payments.Include(p => p.User).FirstOrDefaultAsync(p => p.Id == paymentId);
        if (payment == null) return null;

        if (Enum.TryParse<PaymentStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            payment.Status = parsedStatus;
            if (parsedStatus == PaymentStatus.Completed && payment.PaidAt == null)
                payment.PaidAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
        return MapToDto(payment);
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
