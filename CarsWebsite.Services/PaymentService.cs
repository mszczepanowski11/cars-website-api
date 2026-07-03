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
        { (ServiceType.Top, 7),      (19.99m, "Wyróżnienie TOP - 7 dni") },
        { (ServiceType.Top, 14),     (29.99m, "Wyróżnienie TOP - 14 dni") },
        { (ServiceType.Top, 30),     (49.99m, "Wyróżnienie TOP - 30 dni") },
        { (ServiceType.Premium, 7),  (29.99m, "Oferta Premium - 7 dni") },
        { (ServiceType.Premium, 14), (44.99m, "Oferta Premium - 14 dni") },
        { (ServiceType.Premium, 30), (79.99m, "Oferta Premium - 30 dni") },
        { (ServiceType.Featured, 7),  (14.99m, "Wyróżnienie - 7 dni") },
        { (ServiceType.Featured, 14), (24.99m, "Wyróżnienie - 14 dni") },
        { (ServiceType.Featured, 30), (39.99m, "Wyróżnienie - 30 dni") },
        { (ServiceType.Refresh, 1),   (4.99m,  "Odświeżenie ogłoszenia") },
        { (ServiceType.EventFeatured, 7),  (9.99m,  "Wyróżnienie wydarzenia - 7 dni") },
        { (ServiceType.EventFeatured, 14), (17.99m, "Wyróżnienie wydarzenia - 14 dni") },
        { (ServiceType.EventFeatured, 30), (29.99m, "Wyróżnienie wydarzenia - 30 dni") },
        // Subscription packages — key int = (int)SubscriptionTier
        { (ServiceType.Subscription, (int)SubscriptionTier.Start),   (SubscriptionPlanConfig.GetBruttoPrice(SubscriptionTier.Start),   "Pakiet Start - 1 miesiąc") },
        { (ServiceType.Subscription, (int)SubscriptionTier.Biznes),  (SubscriptionPlanConfig.GetBruttoPrice(SubscriptionTier.Biznes),  "Pakiet Biznes - 1 miesiąc") },
        { (ServiceType.Subscription, (int)SubscriptionTier.Premium), (SubscriptionPlanConfig.GetBruttoPrice(SubscriptionTier.Premium), "Pakiet Premium - 1 miesiąc") },
    };

    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly INotificationService _notifications;
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        AppDbContext context,
        IConfiguration config,
        INotificationService notifications,
        ISubscriptionService subscriptionService,
        ILogger<PaymentService> logger)
    {
        _context = context;
        _config = config;
        _notifications = notifications;
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    // Launch promo: every paid service (boosts + B2B subscriptions) activates for free while
    // now < Promotion:FreeUntilUtc, reusing the exact admin-bypass path below. No manual flip
    // needed at cutover — this simply stops returning true once the configured instant passes.
    public bool IsFreePromoActive()
    {
        var raw = _config["Promotion:FreeUntilUtc"];
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (!DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var freeUntil)) return false;
        return DateTime.UtcNow < freeUntil;
    }

    public DateTime? GetFreePromoEndsAtUtc()
    {
        var raw = _config["Promotion:FreeUntilUtc"];
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var freeUntil) ? freeUntil : null;
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
            "[Payment/Initiate] userId={UserId} serviceType={ServiceType} durationDays={Days} advertId={AdvertId} eventId={EventId} subTier={Tier}",
            userId, dto.ServiceType, dto.DurationDays, dto.AdvertId, dto.EventId, dto.SubscriptionTier);

        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("Użytkownik nie istnieje.");

        ServicePriceDto priceInfo;
        if (dto.ServiceType == ServiceType.Subscription)
        {
            if (!dto.SubscriptionTier.HasValue || dto.SubscriptionTier == SubscriptionTier.None || dto.SubscriptionTier == SubscriptionTier.StartProgram || dto.SubscriptionTier == SubscriptionTier.Enterprise)
                throw new ArgumentException("Wybierz pakiet: Start, Biznes lub Premium.");
            if (!user.IsAdmin && user.AccountType != AccountType.Business)
                throw new InvalidOperationException("Subskrypcje B2B są dostępne wyłącznie dla kont biznesowych.");
            var tierKey = (int)dto.SubscriptionTier.Value;
            priceInfo = await GetServicePriceAsync(ServiceType.Subscription, tierKey);
            dto.DurationDays = 30;
        }
        else
        {
            priceInfo = await GetServicePriceAsync(dto.ServiceType, dto.DurationDays);
        }
        _logger.LogInformation("[Payment/Initiate] price={Price} desc={Desc}", priceInfo.Price, priceInfo.Description);

        // K-1: Verify ownership before creating payment (admin bypasses ownership checks)
        if (!user.IsAdmin)
        {
            if (dto.AdvertId.HasValue)
            {
                var ownerCheck = await _context.CarAdverts.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == dto.AdvertId.Value);
                if (ownerCheck == null)
                {
                    _logger.LogWarning("[Payment/Initiate] advertId={AdvertId} not found in CarAdverts", dto.AdvertId.Value);
                    throw new KeyNotFoundException("Ogłoszenie nie istnieje.");
                }
                if (ownerCheck.UserId != userId)
                    throw new UnauthorizedAccessException("Nie jesteś właścicielem tego ogłoszenia.");
            }
            if (dto.EventId.HasValue)
            {
                var eventCheck = await _context.Events.AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == dto.EventId.Value);
                if (eventCheck == null || eventCheck.CreatedByUserId != userId)
                    throw new UnauthorizedAccessException("Nie jesteś właścicielem tego wydarzenia.");
            }
        }

        // Launch promo grants ONE free boost per account (Top/Premium/Featured/Refresh/
        // EventFeatured) while the promo window is open. Business subscriptions are excluded -
        // dealer/business accounts get their own separate free allowance via the existing
        // StartProgram tier (20 free active ads, see SubscriptionService.ActivateStartProgramAsync).
        var freePromoEligible = IsFreePromoActive()
            && dto.ServiceType != ServiceType.Subscription
            && user.FreePromoBoostUsedAt == null;

        var guidPart = Guid.NewGuid().ToString("N")[..8];
        var orderId = $"CARIZO-{userId}-{DateTime.UtcNow:yyyyMMddHHmmss}-{guidPart}";
        if (orderId.Length > 40) orderId = orderId[..40];

        // For subscription payments, store tier int in DurationDays so ActivateSubscriptionServiceAsync can read it
        var storedDurationDays = dto.ServiceType == ServiceType.Subscription && dto.SubscriptionTier.HasValue
            ? (int)dto.SubscriptionTier.Value
            : dto.DurationDays;

        var payment = new Payment
        {
            UserId = userId,
            AdvertId = dto.AdvertId,
            EventId = dto.EventId,
            ServiceType = dto.ServiceType,
            ServiceDescription = priceInfo.Description,
            Amount = priceInfo.Price,
            Currency = "PLN",
            Status = (user.IsAdmin || freePromoEligible) ? PaymentStatus.Completed : PaymentStatus.Pending,
            ImojeOrderId = orderId,
            DurationDays = storedDurationDays,
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
            _logger.LogError(ex, "[Payment/Initiate] SaveChangesAsync FAILED for Payment orderId={OrderId}", orderId);
            throw new InvalidOperationException("Błąd zapisu płatności. Spróbuj ponownie.");
        }
        _logger.LogInformation("[Payment/Initiate] Payment #{PaymentId} saved", payment.Id);

        // Admin, or launch-promo window (one free boost per account): activate immediately
        // without payment
        if (user.IsAdmin || freePromoEligible)
        {
            await ActivateServiceAsync(payment);
            if (freePromoEligible && !user.IsAdmin)
            {
                await _context.Users.Where(u => u.Id == userId)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.FreePromoBoostUsedAt, DateTime.UtcNow));
            }
            _logger.LogInformation(
                "[Payment/Initiate] {Reason} bypass — service activated instantly for userId={UserId}",
                user.IsAdmin ? "Admin" : "Free promo", userId);
            return new PaymentInitiatedDto
            {
                PaymentId      = payment.Id,
                Amount         = priceInfo.Price,
                OrderId        = orderId,
                AdminActivated = true
            };
        }

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
            && !string.IsNullOrEmpty(internalSecret)
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(configuredInternalSecret),
                System.Text.Encoding.UTF8.GetBytes(internalSecret));

        _logger.LogInformation(
            "[Webhook] orderId={OrderId} status={Status} rawBodyLen={RawLen} hasTransaction={HasTx}",
            dto.ResolvedOrderId, dto.ResolvedStatus, rawBody.Length, dto.Transaction != null);

        if (!isInternalCall && !VerifySignature(rawBody, signature))
        {
            _logger.LogWarning(
                "[Webhook] Odrzucono - nieprawidłowy podpis HMAC. orderId={OrderId} sigLen={SigLen}",
                dto.ResolvedOrderId, signature?.Length ?? 0);
            throw new UnauthorizedAccessException("Nieprawidłowy podpis webhooka.");
        }

        // K-2: Lock payment row to prevent duplicate webhook processing
        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            await _context.Database.ExecuteSqlRawAsync(
                "SELECT 1 FROM `payments` WHERE `ImojeOrderId` = {0} FOR UPDATE", dto.ResolvedOrderId);

            var payment = await _context.Payments
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.ImojeOrderId == dto.ResolvedOrderId);

            if (payment == null)
            {
                _logger.LogWarning("Nie znaleziono płatności dla orderId={OrderId}", dto.ResolvedOrderId);
                await tx.CommitAsync();
                return;
            }

            if (payment.Status == PaymentStatus.Completed)
            {
                await tx.CommitAsync();
                return;
            }

            if (dto.ResolvedStatus is "settled" or "confirmed" or "authorized" or "completed" or "Completed")
            {
                payment.Status = PaymentStatus.Completed;
                payment.PaidAt = DateTime.UtcNow;
                payment.ImojeTransactionId = dto.ResolvedTransactionId;
                await _context.SaveChangesAsync();
                await ActivateServiceAsync(payment);
                await tx.CommitAsync();

                _ = _notifications.NotifyAsync(payment.UserId, EmailNotificationType.PaymentConfirmed,
                    "Płatność potwierdzona",
                    $"Twoja płatność za usługę \"{payment.ServiceDescription}\" w kwocie {payment.Amount:0.00} PLN została pomyślnie zrealizowana.",
                    advertId: payment.AdvertId, paymentId: payment.Id);
            }
            else if (dto.ResolvedStatus is "rejected" or "cancelled" or "error" or "Failed" or "Cancelled" or "Refunded")
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Webhook] Transaction rolled back for orderId={OrderId}", dto.ResolvedOrderId);
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<PagedResult<PaymentResponseDto>> GetUserPaymentsAsync(int userId, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _context.Payments
            .AsNoTracking()
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
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _context.Payments
            .AsNoTracking()
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
        var siteUrl    = section["SiteUrl"] ?? "https://carizo.eu";
        // Imoje__ApiUrl should point to the API server (Railway API URL) for webhook delivery.
        // If not set, falls back to siteUrl (only works if Nuxt proxy forwards /api/* to the API).
        var apiUrl     = section["ApiUrl"] ?? siteUrl;

        var actionUrl = sandbox
            ? "https://sandbox.paywall.imoje.pl/payment"
            : "https://paywall.imoje.pl/payment";

        _logger.LogInformation(
            "[Imoje/Build] serviceId={Sid} merchantId={Mid} keyLen={KeyLen} sandbox={Sandbox} siteUrl={Site} apiUrl={Api}",
            serviceId, merchantId, serviceKey.Length, sandbox, siteUrl, apiUrl);

        if (string.IsNullOrEmpty(serviceId) || string.IsNullOrEmpty(serviceKey))
        {
            _logger.LogWarning("[Imoje/Build] MISSING credentials — serviceId empty={SidEmpty} serviceKey empty={SkEmpty}",
                string.IsNullOrEmpty(serviceId), string.IsNullOrEmpty(serviceKey));
            return ($"{siteUrl}/payment/return?status=pending&paymentId={payment.Id}", new Dictionary<string, string>());
        }

        // Some imoje configurations require non-empty customer name fields.
        var firstName = string.IsNullOrWhiteSpace(user.Name)    ? "Klient" : user.Name;
        var lastName  = string.IsNullOrWhiteSpace(user.Surname) ? "Carizo" : user.Surname;

        var successUrl = payment.AdvertId.HasValue
            ? $"{siteUrl}/payment/return?status=success&paymentId={payment.Id}&advertId={payment.AdvertId}"
            : $"{siteUrl}/payment/return?status=success&paymentId={payment.Id}";

        var fields = new Dictionary<string, string>
        {
            ["amount"]            = ((long)Math.Round(payment.Amount * 100, MidpointRounding.AwayFromZero)).ToString(),
            ["currency"]          = "PLN",
            ["orderId"]           = orderId,
            ["customerFirstName"] = firstName,
            ["customerLastName"]  = lastName,
            ["customerEmail"]     = user.Email ?? "",
            ["urlSuccess"]        = successUrl,
            ["urlFailure"]        = $"{siteUrl}/payment/return?status=failure&paymentId={payment.Id}",
            ["urlReturn"]         = $"{siteUrl}/payment/return?status=cancel&paymentId={payment.Id}",
            ["orderDescription"]  = payment.ServiceDescription,
            ["serviceId"]         = serviceId,
            ["urlNotification"]   = $"{apiUrl}/api/Payment/webhook",
        };

        // merchantId is optional — only include if configured, empty string breaks the signature
        if (!string.IsNullOrEmpty(merchantId))
            fields["merchantId"] = merchantId;

        // Remove empty-value fields before computing signature (PHP SDK behaviour)
        foreach (var key in fields.Where(kv => string.IsNullOrEmpty(kv.Value)).Select(kv => kv.Key).ToList())
            fields.Remove(key);

        fields["signature"] = ComputeImojeSignature(fields, serviceKey);

        _logger.LogInformation(
            "[Imoje/Build] amount={Amount} orderId={OrderId} firstName={First} lastName={Last} email={Email} urlNotification={Notif} fieldKeys={Keys} sigPrefix={Sig}",
            fields["amount"], orderId, firstName, lastName, user.Email,
            fields.GetValueOrDefault("urlNotification", ""), string.Join(",", fields.Keys.Where(k => k != "signature")),
            fields["signature"][..16] + "...");

        return (actionUrl, fields);
    }

    private static string ComputeImojeSignature(Dictionary<string, string> fields, string serviceKey)
    {
        // Match PHP SDK: ksort(), skip empty values, "key=value&...", SHA256(data+serviceKey), ";sha256"
        var sorted = fields
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal);
        var data = string.Join("&", sorted.Select(kv => $"{kv.Key}={kv.Value}"));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data + serviceKey))).ToLower();
        return $"{hash};sha256";
    }

    private async Task ActivateServiceAsync(Payment payment)
    {
        if (payment.ServiceType == ServiceType.Subscription)
        {
            await ActivateSubscriptionServiceAsync(payment);
            return;
        }

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

            // Track monthly featured quota for business subscribers
            if (payment.ServiceType is ServiceType.Top or ServiceType.Premium or ServiceType.Featured)
            {
                try { await _subscriptionService.ConsumeFeatureQuotaAsync(payment.UserId); }
                catch (Exception ex) { _logger.LogWarning(ex, "ConsumeFeatureQuota failed userId={UserId} — ignorowane", payment.UserId); }
            }
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

    private async Task ActivateSubscriptionServiceAsync(Payment payment)
    {
        var tier = (SubscriptionTier)(payment.DurationDays ?? (int)SubscriptionTier.None);
        if (tier == SubscriptionTier.None)
        {
            _logger.LogWarning("[Subscription/Activate] Invalid tier in DurationDays for payment #{PaymentId}", payment.Id);
            return;
        }

        await _subscriptionService.ActivateSubscriptionAsync(payment.UserId, tier);

        _ = _notifications.NotifyAsync(payment.UserId, EmailNotificationType.PromotionActivated,
            $"Pakiet {tier} aktywowany",
            $"Twoja subskrypcja CARIZO {tier} została aktywowana. Możesz teraz korzystać ze wszystkich funkcji pakietu przez 30 dni.",
            paymentId: payment.Id);
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

        _logger.LogInformation(
            "[Webhook/Verify] secretLen={SecretLen} sigHeader={Sig} bodyLen={BodyLen} bodyPrefix={Prefix}",
            secret.Length, signature, rawBody.Length, rawBody.Length > 80 ? rawBody[..80] : rawBody);

        if (string.IsNullOrEmpty(secret))
        {
            _logger.LogWarning("[Webhook/Verify] BRAK klucza — skonfiguruj Imoje__ServiceKey w Railway");
            return false;
        }

        var parts = signature?.Split(';');
        var hashMethod = parts?.Length == 2 ? parts[1].ToLower() : "sha256";
        var receivedHash = parts?[0]?.ToLower() ?? "";

        if (hashMethod != "sha256")
        {
            _logger.LogWarning("[Webhook/Verify] Nieobsługiwana metoda hash: {Method}", hashMethod);
            return false;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody + secret))).ToLower();
        var match = hash == receivedHash;

        _logger.LogInformation(
            "[Webhook/Verify] computed={Computed} received={Received} match={Match}",
            hash[..16] + "...", receivedHash.Length > 16 ? receivedHash[..16] + "..." : receivedHash, match);

        return match;
    }

    public async Task<PaymentResponseDto?> AdminUpdateStatusAsync(int paymentId, string status)
    {
        var payment = await _context.Payments.Include(p => p.User).FirstOrDefaultAsync(p => p.Id == paymentId);
        if (payment == null) return null;

        if (Enum.TryParse<PaymentStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            var wasAlreadyCompleted = payment.Status == PaymentStatus.Completed;
            payment.Status = parsedStatus;
            if (parsedStatus == PaymentStatus.Completed && payment.PaidAt == null)
                payment.PaidAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            if (parsedStatus == PaymentStatus.Completed && !wasAlreadyCompleted)
                await ActivateServiceAsync(payment);
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
