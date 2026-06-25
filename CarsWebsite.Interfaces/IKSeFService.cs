using CarsWebsite;

namespace cars_website_api.CarsWebsite.Interfaces;

public interface IKSeFService
{
    Task<string?> SendInvoiceAsync(Invoice invoice, List<Payment> payments);
}
