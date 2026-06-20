using cars_website_api.CarsWebsite.Interfaces;

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
                _lastRunDate = DateOnly.FromDateTime(now);
                var (invoiceMonth, invoiceYear) = now.Month == 1
                    ? (12, now.Year - 1)
                    : (now.Month - 1, now.Year);

                _logger.LogInformation(
                    "[MonthlyInvoiceJob] Generowanie faktur za {Month}/{Year}",
                    invoiceMonth, invoiceYear);

                try
                {
                    using var scope = _scopeFactory.CreateScope();
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

            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }
}
