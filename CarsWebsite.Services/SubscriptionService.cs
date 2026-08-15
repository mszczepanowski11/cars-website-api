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

    // Every numeric value (price, ad limit, emission days, featured quota) is sourced from
    // SubscriptionPlanConfig - the single source of truth also used to actually enforce these
    // limits at advert-creation/publish/renew time. This used to hardcode its own, separately
    // drifted EmissionDays (30/45/60 vs. the 90 actually enforced everywhere else) - sourcing
    // everything from SubscriptionPlanConfig here is what stops that from happening again.
    public IReadOnlyList<SubscriptionPlanDto> GetPlans()
    {
        SubscriptionPlanDto BuildPlan(SubscriptionTier tier, string name, IEnumerable<string> extraFeatures, bool isCustom = false)
        {
            var limits = SubscriptionPlanConfig.GetLimits(tier);
            var features = new List<string>
            {
                limits.MaxActiveAds == int.MaxValue ? "Nieograniczone ogłoszenia" : $"{limits.MaxActiveAds} aktywnych ogłoszeń",
                $"Emisja {limits.EmissionDays} dni",
                limits.FeaturedQuotaPerMonth == int.MaxValue ? "Nieograniczone wyróżnienia" : $"{limits.FeaturedQuotaPerMonth} wyróżnień/miesiąc",
            };
            features.AddRange(extraFeatures);

            return new SubscriptionPlanDto
            {
                Tier = tier,
                Name = name,
                NettoPrice = SubscriptionPlanConfig.GetNettoPrice(tier),
                BruttoPrice = SubscriptionPlanConfig.GetBruttoPrice(tier),
                MaxActiveAds = limits.MaxActiveAds,
                EmissionDays = limits.EmissionDays,
                FeaturedQuotaPerMonth = limits.FeaturedQuotaPerMonth,
                IsCustom = isCustom,
                Features = features.ToArray(),
            };
        }

        return
        [
            BuildPlan(SubscriptionTier.Start, "Start", ["Profil dealera", "Faktura VAT", "Wsparcie e-mail"]),
            BuildPlan(SubscriptionTier.Biznes, "Biznes", ["Priorytetowe wsparcie", "Faktura VAT"]),
            BuildPlan(SubscriptionTier.Premium, "Premium", ["Dedykowany opiekun", "Faktura VAT", "API dostęp (roadmap)"]),
            BuildPlan(SubscriptionTier.Enterprise, "Enterprise", ["Dedykowany opiekun 24/7", "Indywidualna umowa", "SLA"], isCustom: true),
        ];
    }

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

    public async Task RevokeSubscriptionAsync(int userId)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("Użytkownik nie istnieje.");

        _logger.LogInformation("[Subscription] Revoking tier={Tier} for userId={UserId} (refund)", user.SubscriptionTier, userId);
        user.SubscriptionTier = SubscriptionTier.None;
        user.SubscriptionExpiresAt = null;
        user.FeaturedQuotaUsed = 0;
        user.FeaturedQuotaResetAt = null;

        await _context.SaveChangesAsync();
    }
}
