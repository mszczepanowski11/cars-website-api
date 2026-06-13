using cars_website_api.CarsWebsite.DTOs.Invoice;

namespace cars_website_api.CarsWebsite.Interfaces;

public interface IInvoiceService
{
    Task GenerateMonthlyInvoicesAsync(int month, int year);
    Task<PagedResult<InvoiceResponseDto>> GetUserInvoicesAsync(int userId, int page, int pageSize);
    Task<InvoiceResponseDto> GetInvoiceAsync(int id, int userId);
    Task<byte[]> GenerateInvoiceHtmlAsync(int id, int userId);
    Task<PagedResult<InvoiceResponseDto>> GetAllInvoicesAsync(int page, int pageSize);
    Task SendInvoiceByEmailAsync(int invoiceId);
}
