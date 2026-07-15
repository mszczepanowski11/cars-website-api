using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// BackgroundService uruchamiający generowanie faktur zbiorczych
/// 1-go dnia każdego miesiąca o 02:00 UTC za miesiąc poprzedni.
/// </summary>
public class MonthlyInvoiceJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MonthlyInvoiceJob> _logger;
    private DateOnly _lastRunDate = DateOnly.MinValue;

    public MonthlyInvoiceJob(IServiceScopeFactory scopeFactory, ILogger<MonthlyInvoiceJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;

            if (now.Day == 1 && now.Hour >= 2 && DateOnly.FromDateTime(now) != _lastRunDate)
            {
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

                    // The Invoices.Any() check below is a decent idempotency guard on its own, but
                    // still leaves a race window where two replicas both see "not generated yet"
                    // before either has inserted rows - the advisory lock closes that window so
                    // only one instance ever actually runs GenerateMonthlyInvoicesAsync per month.
                    var ran = await AdvisoryLock.TryRunExclusiveAsync(context, "carizo:monthly_invoice_job", async () =>
                    {
                        var alreadyGenerated = await context.Invoices
                            .AnyAsync(i => i.Month == invoiceMonth && i.Year == invoiceYear);
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
                    });
                    if (ran) _lastRunDate = DateOnly.FromDateTime(now);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[MonthlyInvoiceJob] Błąd generowania faktur za {Month}/{Year}",
                        invoiceMonth, invoiceYear);
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }
}
