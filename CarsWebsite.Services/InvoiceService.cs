using System.Net;
using System.Net.Mail;
using System.Text;
using CarsWebsite;
using cars_website_api.CarsWebsite.DTOs.Invoice;
using cars_website_api.CarsWebsite.DTOs.Payment;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

public class InvoiceService : IInvoiceService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly INotificationService _notifications;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(AppDbContext context, IConfiguration config, INotificationService notifications, ILogger<InvoiceService> logger)
    {
        _context = context;
        _config = config;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task GenerateMonthlyInvoicesAsync(int month, int year)
    {
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);
        var monthName = new System.Globalization.CultureInfo("pl-PL").DateTimeFormat.GetMonthName(month);

        var payments = await _context.Payments
            .Include(p => p.User)
            .Where(p => p.Status == PaymentStatus.Completed
                     && p.PaidAt >= start && p.PaidAt < end
                     && p.InvoiceId == null)
            .ToListAsync();

        if (!payments.Any())
        {
            _logger.LogInformation("Brak płatności do zafakturowania za {Month}/{Year}", month, year);
            return;
        }

        var existingCount = await _context.Invoices
            .Where(i => i.Month == month && i.Year == year)
            .CountAsync();

        var seq = existingCount + 1;

        foreach (var group in payments.GroupBy(p => p.UserId))
        {
            var user = group.First().User;
            var groupPayments = group.ToList();
            var gross = groupPayments.Sum(p => p.Amount);
            const decimal vatRate = 0.23m;
            var net = Math.Round(gross / (1 + vatRate), 2);

            var invoice = new Invoice
            {
                UserId = group.Key,
                InvoiceNumber = $"FZ/{year}/{month:D2}/{seq:D4}",
                Month = month,
                Year = year,
                TotalAmount = gross,
                NetAmount = net,
                VatAmount = gross - net,
                VatRate = vatRate,
                Status = InvoiceStatus.Generated,
                GeneratedAt = DateTime.UtcNow
            };

            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync();

            foreach (var p in groupPayments)
                p.InvoiceId = invoice.Id;

            await _context.SaveChangesAsync();

            invoice.User = user;
            invoice.Payments = groupPayments;

            await SendInvoiceEmailAsync(invoice, user);

            _ = _notifications.NotifyAsync(group.Key, EmailNotificationType.InvoiceGenerated,
                "Faktura wygenerowana",
                $"Twoja faktura zbiorcza {invoice.InvoiceNumber} za {monthName} {year} została wygenerowana. Łączna kwota: {invoice.TotalAmount:0.00} PLN.",
                invoiceId: invoice.Id);

            seq++;
        }

        _logger.LogInformation("Wygenerowano faktury za {Month}/{Year}", month, year);
    }

    public async Task<PagedResult<InvoiceResponseDto>> GetUserInvoicesAsync(int userId, int page, int pageSize)
    {
        var query = _context.Invoices
            .Include(i => i.Payments)
            .Include(i => i.User)
            .Where(i => i.UserId == userId)
            .OrderByDescending(i => i.Year).ThenByDescending(i => i.Month);

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<InvoiceResponseDto>
        {
            Items = items.Select(MapToDto).ToList(),
            TotalCount = total
        };
    }

    public async Task<InvoiceResponseDto> GetInvoiceAsync(int id, int userId, bool isAdmin = false)
    {
        var invoice = isAdmin
            ? await _context.Invoices.Include(i => i.Payments).Include(i => i.User)
                .FirstOrDefaultAsync(i => i.Id == id)
            : await _context.Invoices.Include(i => i.Payments).Include(i => i.User)
                .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId);

        return invoice != null
            ? MapToDto(invoice)
            : throw new KeyNotFoundException("Faktura nie istnieje.");
    }

    public async Task<byte[]> GenerateInvoiceHtmlAsync(int id, int userId, bool isAdmin = false)
    {
        var invoice = isAdmin
            ? await _context.Invoices.Include(i => i.Payments).Include(i => i.User)
                .FirstOrDefaultAsync(i => i.Id == id)
            : await _context.Invoices.Include(i => i.Payments).Include(i => i.User)
                .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId);

        if (invoice == null) throw new KeyNotFoundException("Faktura nie istnieje.");

        return Encoding.UTF8.GetBytes(BuildInvoiceHtml(invoice));
    }

    public async Task<byte[]> GenerateInvoicePdfAsync(int id, int userId, bool isAdmin = false)
    {
        var invoice = isAdmin
            ? await _context.Invoices.Include(i => i.Payments).Include(i => i.User)
                .FirstOrDefaultAsync(i => i.Id == id)
            : await _context.Invoices.Include(i => i.Payments).Include(i => i.User)
                .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId);

        if (invoice == null) throw new KeyNotFoundException("Faktura nie istnieje.");

        QuestPDF.Settings.License = LicenseType.Community;

        var ci = new System.Globalization.CultureInfo("pl-PL");
        var monthName = ci.DateTimeFormat.GetMonthName(invoice.Month);
        var user = invoice.User;
        var buyerName = user?.AccountType == AccountType.Business && !string.IsNullOrWhiteSpace(user.CompanyName)
            ? user.CompanyName : $"{user?.Name} {user?.Surname}";

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(40);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                page.Content().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Faktura zbiorcza").FontSize(20).Bold();
                            c.Item().Text($"Nr: {invoice.InvoiceNumber}").FontSize(11);
                            c.Item().Text($"Okres: {monthName} {invoice.Year}").FontSize(10).FontColor(Colors.Grey.Medium);
                            c.Item().Text($"Wystawiono: {invoice.GeneratedAt:dd.MM.yyyy}").FontSize(10).FontColor(Colors.Grey.Medium);
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("CARIZO Wiktor Niezgoda").Bold().AlignRight();
                            c.Item().Text("NIP: 9452331007").AlignRight();
                            c.Item().Text("ul. H. Pachońskiego 7/60, 31-223 Kraków").AlignRight();
                        });
                    });

                    col.Item().PaddingVertical(12).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("NABYWCA").FontSize(8).FontColor(Colors.Grey.Medium);
                            c.Item().Text(buyerName).Bold();
                            if (user?.AccountType == AccountType.Business && !string.IsNullOrWhiteSpace(user.Nip))
                                c.Item().Text($"NIP: {user.Nip}");
                            c.Item().Text(user?.Email ?? "");
                        });
                    });

                    col.Item().PaddingTop(16).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(25);
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(1);
                            columns.ConstantColumn(90);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(6).Text("Lp.").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(6).Text("Opis usługi").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(6).Text("Data").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(6).Text("Kwota brutto").Bold().AlignRight();
                        });

                        var i = 1;
                        foreach (var p in invoice.Payments)
                        {
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(6).Text(i++.ToString());
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(6).Text(p.ServiceDescription);
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(6).Text(p.PaidAt?.ToString("dd.MM.yyyy") ?? "–");
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(6).Text($"{p.Amount:0.00} PLN").AlignRight();
                        }
                    });

                    col.Item().PaddingTop(12).AlignRight().Column(c =>
                    {
                        c.Item().Text($"Wartość netto: {invoice.NetAmount:0.00} PLN");
                        c.Item().Text($"VAT 23%: {invoice.VatAmount:0.00} PLN");
                        c.Item().Text($"Razem do zapłaty: {invoice.TotalAmount:0.00} PLN").FontSize(13).Bold();
                    });

                    col.Item().PaddingTop(30).Text("Dokument wygenerowany automatycznie przez system CARIZO · carizo.pl")
                        .FontSize(8).FontColor(Colors.Grey.Medium).AlignCenter();
                });
            });
        }).GeneratePdf();
    }

    public async Task<PagedResult<InvoiceResponseDto>> GetAllInvoicesAsync(int page, int pageSize)
    {
        var query = _context.Invoices
            .Include(i => i.Payments)
            .Include(i => i.User)
            .OrderByDescending(i => i.GeneratedAt);

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<InvoiceResponseDto>
        {
            Items = items.Select(MapToDto).ToList(),
            TotalCount = total
        };
    }

    public async Task SendInvoiceByEmailAsync(int invoiceId)
    {
        var invoice = await _context.Invoices
            .Include(i => i.Payments)
            .Include(i => i.User)
            .FirstOrDefaultAsync(i => i.Id == invoiceId)
            ?? throw new KeyNotFoundException("Faktura nie istnieje.");

        await SendInvoiceEmailAsync(invoice, invoice.User);
    }

    private async Task SendInvoiceEmailAsync(Invoice invoice, User user)
    {
        var smtpSection = _config.GetSection("Smtp");
        var host = smtpSection["Host"];

        if (string.IsNullOrEmpty(host))
        {
            _logger.LogWarning("SMTP nie skonfigurowany – pominięto wysyłkę faktury {Number}", invoice.InvoiceNumber);
            return;
        }

        var html = BuildInvoiceHtml(invoice);
        var from = smtpSection["From"] ?? "faktury@carizo.pl";
        var adminEmail = _config["Admin:Email"] ?? "admin@carizo.pl";
        var port = int.TryParse(smtpSection["Port"], out var p) ? p : 587;

        try
        {
            using var smtpClient = new SmtpClient(host, port)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(smtpSection["User"], smtpSection["Password"])
            };

            await smtpClient.SendMailAsync(new MailMessage(from, user.Email,
                $"Faktura zbiorcza CARIZO – {invoice.InvoiceNumber}", html)
                { IsBodyHtml = true });

            await smtpClient.SendMailAsync(new MailMessage(from, adminEmail,
                $"[KOPIA] Faktura {invoice.InvoiceNumber} – {user.Email}", html)
                { IsBodyHtml = true });

            invoice.Status = InvoiceStatus.Sent;
            invoice.SentAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _ = _notifications.NotifyAsync(invoice.UserId, EmailNotificationType.InvoiceSent,
                "Faktura wysłana",
                $"Faktura {invoice.InvoiceNumber} została wysłana na adres {user.Email}.",
                invoiceId: invoice.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd wysyłki e-mail faktury {Number}", invoice.InvoiceNumber);
        }
    }

    private static string BuildInvoiceHtml(Invoice inv)
    {
        var ci = new System.Globalization.CultureInfo("pl-PL");
        var monthName = ci.DateTimeFormat.GetMonthName(inv.Month);
        var user = inv.User;

        var buyerName = user?.AccountType == AccountType.Business
                        && !string.IsNullOrWhiteSpace(user.CompanyName)
            ? user.CompanyName
            : $"{user?.Name} {user?.Surname}";

        var sb = new StringBuilder();
        sb.Append(@"<!DOCTYPE html>
<html><head><meta charset=""UTF-8"">
<style>
body{font-family:Arial,sans-serif;color:#111;max-width:740px;margin:0 auto;padding:32px}
h1{font-size:24px;margin:0 0 4px}
.sub{color:#666;font-size:13px;margin-bottom:24px}
hr{border:none;border-top:1px solid #ddd;margin:20px 0}
.parties{display:flex;gap:40px;margin-bottom:24px;font-size:13px}
.party h4{margin:0 0 6px;font-size:11px;color:#888;text-transform:uppercase;letter-spacing:.5px}
.party p{margin:2px 0}
table{width:100%;border-collapse:collapse;margin-top:8px}
th{text-align:left;padding:9px 12px;background:#f5f5f5;border:1px solid #ddd;font-size:12px;text-transform:uppercase;color:#555}
td{padding:9px 12px;border:1px solid #ddd;font-size:13px}
.totals{margin-top:20px;text-align:right;font-size:13px}
.totals div{margin:4px 0}
.total-final{font-size:16px;font-weight:700;margin-top:10px}
.footer{margin-top:40px;font-size:11px;color:#999;border-top:1px solid #eee;padding-top:12px;text-align:center}
</style></head><body>
");

        sb.Append($"<h1>Faktura zbiorcza</h1>");
        sb.Append($"<div class=\"sub\">Nr: <strong>{inv.InvoiceNumber}</strong> &nbsp;·&nbsp; Okres: {monthName} {inv.Year} &nbsp;·&nbsp; Wystawiono: {inv.GeneratedAt:dd.MM.yyyy}</div>");
        sb.Append("<hr/>");

        sb.Append("<div class=\"parties\">");
        sb.Append($"<div class=\"party\"><h4>Nabywca</h4><p><strong>{buyerName}</strong></p>");
        if (user?.AccountType == AccountType.Business && !string.IsNullOrWhiteSpace(user.Nip))
            sb.Append($"<p>NIP: {user.Nip}</p>");
        sb.Append($"<p>{user?.Email}</p></div>");
        sb.Append($"<div class=\"party\"><h4>Sprzedawca</h4>" +
                  $"<p><strong>CARIZO Wiktor Niezgoda</strong></p>" +
                  $"<p>NIP: 9452331007</p><p>REGON: 544870688</p>" +
                  $"<p>ul. Henryka Pachońskiego 7/60, 31-223 Kraków</p></div>");
        sb.Append("</div>");

        sb.Append("<table><thead><tr><th>Lp.</th><th>Opis usługi</th><th>Data</th><th style=\"text-align:right\">Kwota brutto</th></tr></thead><tbody>");
        var i = 1;
        foreach (var p in inv.Payments)
            sb.Append($"<tr><td>{i++}</td><td>{p.ServiceDescription}</td><td>{p.PaidAt?.ToString("dd.MM.yyyy") ?? "–"}</td><td style=\"text-align:right\">{p.Amount:0.00} PLN</td></tr>");
        sb.Append("</tbody></table>");

        sb.Append($@"
<div class=""totals"">
  <div>Wartość netto: {inv.NetAmount:0.00} PLN</div>
  <div>VAT 23%: {inv.VatAmount:0.00} PLN</div>
  <div class=""total-final"">Razem do zapłaty: {inv.TotalAmount:0.00} PLN</div>
</div>
<div class=""footer"">Faktura wygenerowana automatycznie przez system CARIZO &nbsp;·&nbsp; carizo.pl</div>
</body></html>");

        return sb.ToString();
    }

    private static InvoiceResponseDto MapToDto(Invoice inv) => new()
    {
        Id = inv.Id,
        InvoiceNumber = inv.InvoiceNumber,
        Month = inv.Month,
        Year = inv.Year,
        TotalAmount = inv.TotalAmount,
        NetAmount = inv.NetAmount,
        VatAmount = inv.VatAmount,
        VatRate = inv.VatRate,
        Status = inv.Status,
        GeneratedAt = inv.GeneratedAt,
        SentAt = inv.SentAt,
        UserName = $"{inv.User?.Name} {inv.User?.Surname}",
        UserEmail = inv.User?.Email ?? string.Empty,
        CompanyName = inv.User?.CompanyName,
        Nip = inv.User?.Nip,
        Items = inv.Payments.Select(p => new PaymentResponseDto
        {
            Id = p.Id,
            ServiceType = p.ServiceType,
            ServiceDescription = p.ServiceDescription,
            Amount = p.Amount,
            Currency = p.Currency,
            Status = p.Status,
            ImojeTransactionId = p.ImojeTransactionId,
            CreatedAt = p.CreatedAt,
            PaidAt = p.PaidAt,
            AdvertId = p.AdvertId,
            DurationDays = p.DurationDays
        }).ToList()
    };
}
