using CarsWebsite;
using cars_website_api.CarsWebsite.DTOs.Subscription;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.EntityFrameworkCore;

public class SubscriptionService : ISubscriptionService
{
    private readonly AppDbContext _context;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(AppDbContext context, ILogger<SubscriptionService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public IReadOnlyList<SubscriptionPlanDto> GetPlans() =>
    [
        new SubscriptionPlanDto
        {
            Tier = SubscriptionTier.Start,
            Name = "Start",
            NettoPrice = SubscriptionPlanConfig.GetNettoPrice(SubscriptionTier.Start),
            BruttoPrice = SubscriptionPlanConfig.GetBruttoPrice(SubscriptionTier.Start),
            MaxActiveAds = 25,
            EmissionDays = 30,
            FeaturedQuotaPerMonth = 3,
            Features =
            [
                "25 aktywnych ogłoszeń",
                "Emisja 30 dni",
                "3 wyróżnienia/miesiąc",
                "Profil dealera",
                "Faktura VAT",
            ],
        },
        new SubscriptionPlanDto
        {
            Tier = SubscriptionTier.Biznes,
            Name = "Biznes",
            NettoPrice = SubscriptionPlanConfig.GetNettoPrice(SubscriptionTier.Biznes),
            BruttoPrice = SubscriptionPlanConfig.GetBruttoPrice(SubscriptionTier.Biznes),
            MaxActiveAds = 75,
            EmissionDays = 45,
            FeaturedQuotaPerMonth = 10,
            Features =
            [
                "75 aktywnych ogłoszeń",
                "Emisja 45 dni",
                "10 wyróżnień/miesiąc",
                "Priorytetowe wsparcie",
                "Faktura VAT",
            ],
        },
        new SubscriptionPlanDto
        {
            Tier = SubscriptionTier.Premium,
            Name = "Premium",
            NettoPrice = SubscriptionPlanConfig.GetNettoPrice(SubscriptionTier.Premium),
            BruttoPrice = SubscriptionPlanConfig.GetBruttoPrice(SubscriptionTier.Premium),
            MaxActiveAds = 200,
            EmissionDays = 60,
            FeaturedQuotaPerMonth = 30,
            Features =
            [
                "200 aktywnych ogłoszeń",
                "Emisja 60 dni",
                "30 wyróżnień/miesiąc",
                "Dedykowany opiekun",
                "Faktura VAT",
                "API dostęp (roadmap)",
            ],
        },
        new SubscriptionPlanDto
        {
            Tier = SubscriptionTier.Enterprise,
            Name = "Enterprise",
            NettoPrice = 0,
            BruttoPrice = 0,
            MaxActiveAds = int.MaxValue,
            EmissionDays = 90,
            FeaturedQuotaPerMonth = int.MaxValue,
            IsCustom = true,
            Features =
            [
                "Nieograniczone ogłoszenia",
                "Emisja 90 dni",
                "Nieograniczone wyróżnienia",
                "Dedykowany opiekun 24/7",
                "Indywidualna umowa",
                "SLA",
            ],
        },
    ];

    public async Task<SubscriptionStatusDto> GetMySubscriptionAsync(int userId)
    {
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("Użytkownik nie istnieje.");

        var tier = user.SubscriptionTier;
        var isExpired = user.SubscriptionExpiresAt.HasValue && user.SubscriptionExpiresAt < DateTime.UtcNow;
        var effectiveTier = isExpired ? SubscriptionTier.None : tier;
        var limits = SubscriptionPlanConfig.GetLimits(effectiveTier);

        var quotaUsed = user.FeaturedQuotaUsed;
        var quotaReset = user.FeaturedQuotaResetAt;
        if (quotaReset.HasValue && quotaReset < DateTime.UtcNow)
        {
            quotaUsed = 0;
        }

        var quotaMax = limits.FeaturedQuotaPerMonth;
        var quotaRemaining = quotaMax == int.MaxValue ? int.MaxValue : Math.Max(0, quotaMax - quotaUsed);

        return new SubscriptionStatusDto
        {
            Tier = effectiveTier,
            TierName = effectiveTier switch
            {
                SubscriptionTier.StartProgram => "Program Start",
                SubscriptionTier.Start        => "Start",
                SubscriptionTier.Biznes       => "Biznes",
                SubscriptionTier.Premium      => "Premium",
                SubscriptionTier.Enterprise   => "Enterprise",
                _                             => "Brak subskrypcji",
            },
            IsActive = effectiveTier != SubscriptionTier.None,
            ExpiresAt = user.SubscriptionExpiresAt,
            StartedAt = user.SubscriptionStartedAt,
            IsStartProgram = effectiveTier == SubscriptionTier.StartProgram,
            IsVerifiedDealer = user.IsVerifiedDealer,
            MaxActiveAds = limits.MaxActiveAds == int.MaxValue ? -1 : limits.MaxActiveAds,
            EmissionDays = limits.EmissionDays,
            FeaturedQuotaPerMonth = quotaMax == int.MaxValue ? -1 : quotaMax,
            FeaturedQuotaUsed = quotaUsed,
            FeaturedQuotaRemaining = quotaRemaining == int.MaxValue ? -1 : quotaRemaining,
            FeaturedQuotaResetAt = quotaReset,
        };
    }

    public async Task ActivateSubscriptionAsync(int userId, SubscriptionTier tier)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("Użytkownik nie istnieje.");

        var now = DateTime.UtcNow;

        // Extend if already active and same or upgraded tier, otherwise reset
        var baseDate = user.SubscriptionExpiresAt.HasValue && user.SubscriptionExpiresAt > now && user.SubscriptionTier == tier
            ? user.SubscriptionExpiresAt.Value
            : now;

        user.SubscriptionTier = tier;
        user.SubscriptionExpiresAt = baseDate.AddDays(30);
        user.SubscriptionStartedAt ??= now;
        user.FeaturedQuotaUsed = 0;
        user.FeaturedQuotaResetAt = now.AddDays(30);
        user.IsVerifiedDealer = true;

        await _context.SaveChangesAsync();
        _logger.LogInformation("[Subscription] Activated tier={Tier} for userId={UserId} expiresAt={Exp}", tier, userId, user.SubscriptionExpiresAt);
    }

    public async Task ActivateStartProgramAsync(int userId)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("Użytkownik nie istnieje.");

        if (user.AccountType != AccountType.Business)
            throw new InvalidOperationException("Program Start jest dostępny tylko dla kont biznesowych.");

        if (user.StartProgramActivatedAt.HasValue)
            throw new InvalidOperationException("Program Start został już aktywowany na tym koncie.");

        if (user.SubscriptionTier != SubscriptionTier.None)
            throw new InvalidOperationException("Konto ma już aktywną subskrypcję.");

        var now = DateTime.UtcNow;
        user.StartProgramActivatedAt = now;
        user.SubscriptionTier = SubscriptionTier.StartProgram;
        user.SubscriptionStartedAt = now;
        user.SubscriptionExpiresAt = now.AddDays(90); // 3 months
        user.FeaturedQuotaUsed = 0;
        user.FeaturedQuotaResetAt = now.AddDays(30);
        user.IsVerifiedDealer = true;

        await _context.SaveChangesAsync();
        _logger.LogInformation("[Subscription] StartProgram activated for userId={UserId}", userId);
    }

    public async Task<(bool CanCreate, string? Error)> CheckActiveAdLimitAsync(int userId)
    {
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return (false, "Użytkownik nie istnieje.");

        if (user.IsAdmin) return (true, null);

        if (user.AccountType != AccountType.Business) return (true, null); // personal handled separately

        var tier = user.SubscriptionExpiresAt.HasValue && user.SubscriptionExpiresAt < DateTime.UtcNow
            ? SubscriptionTier.None
            : user.SubscriptionTier;

        var maxAds = SubscriptionPlanConfig.GetMaxActiveAds(tier);

        if (maxAds == int.MaxValue) return (true, null);

        var activeCount = await _context.CarAdverts
            .CountAsync(a => a.UserId == userId && a.IsActive && !a.IsHidden);

        if (activeCount >= maxAds)
        {
            var tierName = tier == SubscriptionTier.None
                ? "bez subskrypcji"
                : tier.ToString();
            return (false, $"Osiągnięto limit aktywnych ogłoszeń dla pakietu {tierName} ({maxAds} szt.). Przejdź na wyższy pakiet lub usuń nieaktywne ogłoszenia.");
        }

        return (true, null);
    }

    public async Task ConsumeFeatureQuotaAsync(int userId)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("Użytkownik nie istnieje.");

        if (user.IsAdmin) return;

        if (user.AccountType != AccountType.Business) return; // personal users have no quota

        var tier = user.SubscriptionExpiresAt.HasValue && user.SubscriptionExpiresAt < DateTime.UtcNow
            ? SubscriptionTier.None
            : user.SubscriptionTier;

        var maxQuota = SubscriptionPlanConfig.GetFeaturedQuota(tier);

        // Reset quota if period expired
        if (user.FeaturedQuotaResetAt.HasValue && user.FeaturedQuotaResetAt < DateTime.UtcNow)
        {
            user.FeaturedQuotaUsed = 0;
            user.FeaturedQuotaResetAt = DateTime.UtcNow.AddDays(30);
        }

        if (maxQuota != int.MaxValue && user.FeaturedQuotaUsed >= maxQuota)
            throw new InvalidOperationException($"Wyczerpano miesięczny limit wyróżnień ({maxQuota} szt.) dla Twojego pakietu.");

        user.FeaturedQuotaUsed++;
        await _context.SaveChangesAsync();
    }

    public async Task ResetExpiredSubscriptionsAsync()
    {
        var now = DateTime.UtcNow;
        var expired = await _context.Users
            .Where(u => u.SubscriptionTier != SubscriptionTier.None
                     && u.SubscriptionExpiresAt.HasValue
                     && u.SubscriptionExpiresAt < now)
            .ToListAsync();

        foreach (var user in expired)
        {
            _logger.LogInformation("[Subscription] Expiring tier={Tier} for userId={UserId}", user.SubscriptionTier, user.Id);
            user.SubscriptionTier = SubscriptionTier.None;
            user.SubscriptionExpiresAt = null;
            user.FeaturedQuotaUsed = 0;
            user.FeaturedQuotaResetAt = null;
        }

        if (expired.Count > 0)
            await _context.SaveChangesAsync();
    }
}
