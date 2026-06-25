using cars_website_api.CarsWebsite.DTOs.Financing;

namespace cars_website_api.CarsWebsite.Interfaces;

public interface IFinancingService
{
    Task<int> CreateInquiryAsync(CreateFinancingInquiryDto dto, int? userId);
}
