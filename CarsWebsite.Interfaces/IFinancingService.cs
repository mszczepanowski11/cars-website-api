using cars_website_api.CarsWebsite.DTOs.Financing;

namespace cars_website_api.CarsWebsite.Interfaces;

public interface IFinancingService
{
    Task<int> CreateInquiryAsync(CreateFinancingInquiryDto dto, int? userId);

    // Split into two independently Hangfire-enqueueable jobs (CTO audit Etap 4: durable queue
    // instead of fire-and-forget) - each re-fetches the FinancingInquiry by id itself rather than
    // taking the EF entity directly, since Hangfire JSON-serializes job arguments and a tracked
    // entity graph doesn't survive that round trip cleanly. No-ops if the inquiry no longer exists
    // or (for the user confirmation) never had an email address - safe to enqueue unconditionally.
    Task SendFinancingAdminNotificationAsync(int inquiryId, string advertTitle);
    Task SendFinancingUserConfirmationAsync(int inquiryId, string advertTitle);
}
