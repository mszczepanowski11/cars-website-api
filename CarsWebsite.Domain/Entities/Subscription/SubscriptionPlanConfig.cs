namespace CarsWebsite;

public static class SubscriptionPlanConfig
{
    public record PlanLimits(int MaxActiveAds, int EmissionDays, int FeaturedQuotaPerMonth);

    // EmissionDays standardized to 35 days across every tier (business decision, August 2026 -
    // replaces the previous 90-day standard everywhere: free dealer listings, every paid B2B
    // package, and Enterprise's default). Other limits (ad count, featured quota) still differ by
    // tier. The unsubscribed/no-package fallback ("_") is the free tier for dealers/komisy - 10
    // concurrently active adverts, matching the "10 darmowych ogłoszeń" business requirement.
    public static PlanLimits GetLimits(SubscriptionTier tier) => tier switch
    {
        SubscriptionTier.StartProgram => new(10,         35, 3),
        SubscriptionTier.Start        => new(25,         35, 3),
        SubscriptionTier.Biznes       => new(75,         35, 10),
        SubscriptionTier.Premium      => new(200,        35, 30),
        SubscriptionTier.Enterprise   => new(int.MaxValue, 35, int.MaxValue),
        _                             => new(10,         35, 0),  // unsubscribed B2B / free dealer tier
    };

    public static int GetEmissionDays(SubscriptionTier tier) => GetLimits(tier).EmissionDays;
    public static int GetMaxActiveAds(SubscriptionTier tier) => GetLimits(tier).MaxActiveAds;
    public static int GetFeaturedQuota(SubscriptionTier tier) => GetLimits(tier).FeaturedQuotaPerMonth;

    // Single place that decides how many emission days an advert gets, for both business accounts
    // (tier-based, expired subscription treated as None) and personal accounts (the same "None"
    // tier's days - personal accounts don't have a SubscriptionTier of their own). Every call site
    // that sets/extends an advert's ExpiresAt (creation, publish/republish, renew, admin
    // reactivation, partner-import re-sync) must go through this so none of them can silently drift
    // back to a stale hardcoded day count the way they did before this existed.
    public static int ResolveEmissionDays(User? user)
    {
        if (user?.AccountType != AccountType.Business)
            return GetEmissionDays(SubscriptionTier.None);

        var tier = user.SubscriptionExpiresAt.HasValue && user.SubscriptionExpiresAt < DateTime.UtcNow
            ? SubscriptionTier.None
            : user.SubscriptionTier;
        return GetEmissionDays(tier);
    }

    // Netto monthly prices (PLN)
    public static decimal GetNettoPrice(SubscriptionTier tier) => tier switch
    {
        SubscriptionTier.Start      => 99.00m,
        SubscriptionTier.Biznes     => 279.00m,
        SubscriptionTier.Premium    => 599.00m,
        SubscriptionTier.Enterprise => 0m,      // custom / contact
        _                           => 0m,
    };

    // Brutto = netto * 1.23 (VAT 23%)
    public static decimal GetBruttoPrice(SubscriptionTier tier) => Math.Round(GetNettoPrice(tier) * 1.23m, 2);
}
