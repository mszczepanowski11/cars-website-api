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
}
