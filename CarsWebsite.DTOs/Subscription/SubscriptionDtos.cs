using CarsWebsite;

namespace cars_website_api.CarsWebsite.DTOs.Subscription;

public class SubscriptionStatusDto
{
    public SubscriptionTier Tier { get; set; }
    public string TierName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public bool IsStartProgram { get; set; }
    public bool IsVerifiedDealer { get; set; }

    // Limits
    public int MaxActiveAds { get; set; }
    public int EmissionDays { get; set; }
    public int FeaturedQuotaPerMonth { get; set; }
    public int FeaturedQuotaUsed { get; set; }
    public int FeaturedQuotaRemaining { get; set; }
    public DateTime? FeaturedQuotaResetAt { get; set; }
}

public class SubscriptionPlanDto
{
    public SubscriptionTier Tier { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal NettoPrice { get; set; }
    public decimal BruttoPrice { get; set; }
    public int MaxActiveAds { get; set; }
    public int EmissionDays { get; set; }
    public int FeaturedQuotaPerMonth { get; set; }
    public string[] Features { get; set; } = [];
    public bool IsCustom { get; set; }
}
