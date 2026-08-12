using CarsWebsite;
using cars_website_api.CarsWebsite.DTOs.Subscription;

namespace cars_website_api.CarsWebsite.Interfaces;

public interface ISubscriptionService
{
    IReadOnlyList<SubscriptionPlanDto> GetPlans();
    Task<SubscriptionStatusDto> GetMySubscriptionAsync(int userId);
    Task ActivateSubscriptionAsync(int userId, SubscriptionTier tier);
    Task ActivateStartProgramAsync(int userId);
    Task<(bool CanCreate, string? Error)> CheckActiveAdLimitAsync(int userId);
    Task ConsumeFeatureQuotaAsync(int userId);
    Task ResetExpiredSubscriptionsAsync();

    // Immediate cutoff for a refunded subscription payment (CTO audit Etap 3) - not "subtract the
    // days this payment granted" the way badge/event refunds work, because subscription tiers
    // don't stack multiple time-boxed grants the way ActivateSubscriptionAsync's own "extend if
    // same tier" comment describes; a refund on the payment that (re)activated the tier means the
    // tier itself was never legitimately paid for, so it goes to None immediately. Same reset shape
    // as ResetExpiredSubscriptionsAsync, just for one user on demand instead of the whole expired batch.
    Task RevokeSubscriptionAsync(int userId);
}
