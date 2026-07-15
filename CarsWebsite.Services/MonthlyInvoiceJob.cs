using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Hangfire recurring job (see Program.cs) generujący faktury zbiorcze 1-go dnia każdego
/// miesiąca o 02:00 UTC za miesiąc poprzedni. The Invoices.Any() check is the idempotency
/// guard against a duplicate run; Hangfire's MySQL-backed queue guarantees only one server
/// picks up a given scheduled occurrence in the first place, so the AdvisoryLock this job
/// used to wrap itself in is no longer needed.
/// </summary>
public class MonthlyInvoiceJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MonthlyInvoiceJob> _logger;

    public MonthlyInvoiceJob(IServiceScopeFactory scopeFactory, ILogger<MonthlyInvoiceJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var (invoiceMonth, invoiceYear) = now.Month == 1
            ? (12, now.Year - 1)
            : (now.Month - 1, now.Year);

        _logger.LogInformation(
            "[MonthlyInvoiceJob] Generowanie faktur za {Month}/{Year}",
            invoiceMonth, invoiceYear);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var alreadyGenerated = await context.Invoices
                .AnyAsync(i => i.Month == invoiceMonth && i.Year == invoiceYear, ct);
            if (alreadyGenerated)
            {
                _logger.LogInformation(
                    "[MonthlyInvoiceJob] Faktury za {Month}/{Year} już zostały wygenerowane. Pomijam.",
                    invoiceMonth, invoiceYear);
                return;
            }

            var service = scope.ServiceProvider.GetRequiredService<IInvoiceService>();
            await service.GenerateMonthlyInvoicesAsync(invoiceMonth, invoiceYear);
            _logger.LogInformation(
                "[MonthlyInvoiceJob] Zakończono generowanie faktur za {Month}/{Year}",
                invoiceMonth, invoiceYear);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[MonthlyInvoiceJob] Błąd generowania faktur za {Month}/{Year}",
                invoiceMonth, invoiceYear);
        }
    }
}
