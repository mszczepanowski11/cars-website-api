namespace CarsWebsite;

public static class SubscriptionPlanConfig
{
    public record PlanLimits(int MaxActiveAds, int EmissionDays, int FeaturedQuotaPerMonth);

    // EmissionDays standardized to 90 (3 months) across every tier, matching the platform-wide
    // default advert duration - other limits (ad count, featured quota) still differ by tier.
    public static PlanLimits GetLimits(SubscriptionTier tier) => tier switch
    {
        SubscriptionTier.StartProgram => new(20,         90, 3),
        SubscriptionTier.Start        => new(25,         90, 3),
        SubscriptionTier.Biznes       => new(75,         90, 10),
        SubscriptionTier.Premium      => new(200,        90, 30),
        SubscriptionTier.Enterprise   => new(int.MaxValue, 90, int.MaxValue),
        _                             => new(5,          90, 0),  // unsubscribed B2B grace
    };

    public static int GetEmissionDays(SubscriptionTier tier) => GetLimits(tier).EmissionDays;
    public static int GetMaxActiveAds(SubscriptionTier tier) => GetLimits(tier).MaxActiveAds;
    public static int GetFeaturedQuota(SubscriptionTier tier) => GetLimits(tier).FeaturedQuotaPerMonth;

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
