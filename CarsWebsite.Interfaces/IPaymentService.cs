using cars_website_api.CarsWebsite.DTOs.Payment;
using CarsWebsite;

namespace cars_website_api.CarsWebsite.Interfaces;

public interface IPaymentService
{
    Task<ServicePriceDto> GetServicePriceAsync(ServiceType serviceType, int durationDays);
    Task<PaymentInitiatedDto> InitiatePaymentAsync(InitiatePaymentDto dto, int userId);
    Task HandleWebhookAsync(ImojeWebhookDto dto, string rawBody, string signature, string? internalSecret = null);
    Task<PagedResult<PaymentResponseDto>> GetUserPaymentsAsync(int userId, int page, int pageSize);
    Task<PagedResult<PaymentResponseDto>> GetAllPaymentsAsync(int page, int pageSize);
}
