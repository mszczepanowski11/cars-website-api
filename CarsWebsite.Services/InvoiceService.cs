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
    private readonly IEmailService _email;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(AppDbContext context, IConfiguration config, INotificationService notifications, IEmailService email, ILogger<InvoiceService> logger)
    {
        _context = context;
        _config = config;
        _notifications = notifications;
        _email = email;
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

        // K-3: Serializable isolation prevents duplicate sequence numbers under concurrent calls
        using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var existingNums = await _context.Invoices
                .Where(i => i.Month == month && i.Year == year)
                .Select(i => i.InvoiceNumber)
                .ToListAsync();

            var maxSeq = existingNums.Count == 0 ? 0 :
                existingNums
                    .Select(n => { var p = n.Split('/'); return p.Length == 4 && int.TryParse(p[3], out var s) ? s : 0; })
                    .DefaultIfEmpty(0)
                    .Max();
            var seq = maxSeq + 1;

            foreach (var group in payments.GroupBy(p => p.UserId))
            {
                var user = group.First().User;
                var groupPayments = group.ToList();
                var gross = groupPayments.Sum(p => p.Amount);
                const decimal vatRate = 0.23m;
                var net = Math.Round(gross / (1 + vatRate), 2);
                var vatAmount = Math.Round(net * vatRate, 2);

                var invoice = new Invoice
                {
                    UserId = group.Key,
                    InvoiceNumber = $"FZ/{year}/{month:D2}/{seq:D4}",
                    Month = month,
                    Year = year,
                    TotalAmount = gross,
                    NetAmount = net,
                    VatAmount = vatAmount,
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

            await tx.CommitAsync();
            _logger.LogInformation("Wygenerowano faktury za {Month}/{Year}", month, year);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<PagedResult<InvoiceResponseDto>> GetUserInvoicesAsync(int userId, int page, int pageSize)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
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
        var firstPayment = invoice.Payments.FirstOrDefault();
        var buyerName = !string.IsNullOrWhiteSpace(firstPayment?.BillingName)
            ? firstPayment.BillingName
            : (user?.AccountType == AccountType.Business && !string.IsNullOrWhiteSpace(user.CompanyName)
                ? user.CompanyName : $"{user?.Name} {user?.Surname}");
        var buyerNip = !string.IsNullOrWhiteSpace(firstPayment?.BillingNip)
            ? firstPayment.BillingNip : user?.Nip;
        var buyerAddress = (!string.IsNullOrWhiteSpace(firstPayment?.BillingStreet) || !string.IsNullOrWhiteSpace(firstPayment?.BillingCity))
            ? $"{firstPayment?.BillingStreet}, {firstPayment?.BillingPostalCode} {firstPayment?.BillingCity}".Trim().TrimStart(',').Trim()
            : null;

        var brand   = "#8B0D1D";
        var dark    = "#1a1a1a";
        var muted   = "#666666";
        var light   = "#f7f7f7";
        var border  = "#e0e0e0";

        var sellerName    = _config["Invoice:SellerName"]    ?? "CARIZO Wiktor Niezgoda";
        var sellerNip     = _config["Invoice:SellerNip"]     ?? "9452331007";
        var sellerRegon   = _config["Invoice:SellerRegon"]   ?? "544870688";
        var sellerAddress = _config["Invoice:SellerAddress"] ?? "ul. Henryka Pachońskiego 7/60, 31-223 Kraków";

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(0);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial").FontColor(dark));

                page.Content().Column(col =>
                {
                    // ── Red header bar ────────────────────────────────────────
                    col.Item().Background(brand).Padding(28).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(t =>
                            {
                                t.Span("CARI").FontSize(26).Bold().FontColor(Colors.White);
                                t.Span("ZO").FontSize(26).Bold().FontColor(Colors.White).Underline();
                            });
                            c.Item().Text("platforma motoryzacyjna · carizo.eu")
                                .FontSize(8).FontColor("#e8a0a8");
                        });
                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().Text("FAKTURA ZBIORCZA")
                                .FontSize(14).Bold().FontColor(Colors.White).AlignRight();
                            c.Item().Text($"Nr {invoice.InvoiceNumber}")
                                .FontSize(10).FontColor("#e8a0a8").AlignRight();
                        });
                    });

                    // ── Meta strip ────────────────────────────────────────────
                    col.Item().Background(light).PaddingHorizontal(28).PaddingVertical(10).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("OKRES ROZLICZENIOWY").FontSize(7).FontColor(muted);
                            c.Item().Text($"{monthName} {invoice.Year}").Bold().FontSize(11);
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("DATA WYSTAWIENIA").FontSize(7).FontColor(muted);
                            c.Item().Text(invoice.GeneratedAt.ToString("dd.MM.yyyy")).Bold().FontSize(11);
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("FORMA PŁATNOŚCI").FontSize(7).FontColor(muted);
                            c.Item().Text("Płatność elektroniczna").Bold().FontSize(11);
                        });
                    });

                    col.Item().PaddingHorizontal(28).PaddingTop(20).Row(row =>
                    {
                        // Seller box
                        row.RelativeItem().Border(1).BorderColor(border).Padding(14).Column(c =>
                        {
                            c.Item().Text("SPRZEDAWCA").FontSize(7).FontColor(brand).Bold();
                            c.Item().PaddingTop(4).Text(sellerName).Bold().FontSize(11);
                            c.Item().Text($"NIP: {sellerNip}").FontSize(9).FontColor(muted);
                            c.Item().Text($"REGON: {sellerRegon}").FontSize(9).FontColor(muted);
                            c.Item().PaddingTop(4).Text(sellerAddress).FontSize(9);
                        });

                        row.ConstantItem(16);

                        // Buyer box
                        row.RelativeItem().Border(1).BorderColor(brand).Padding(14).Column(c =>
                        {
                            c.Item().Text("NABYWCA").FontSize(7).FontColor(brand).Bold();
                            c.Item().PaddingTop(4).Text(buyerName).Bold().FontSize(11);
                            if (!string.IsNullOrWhiteSpace(buyerNip))
                                c.Item().Text($"NIP: {buyerNip}").FontSize(9).FontColor(muted);
                            if (!string.IsNullOrWhiteSpace(buyerAddress))
                                c.Item().Text(buyerAddress).FontSize(9);
                            c.Item().PaddingTop(4).Text(user?.Email ?? "").FontSize(9).FontColor(muted);
                        });
                    });

                    // ── Items table ───────────────────────────────────────────
                    col.Item().PaddingHorizontal(28).PaddingTop(20).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(28);
                            columns.RelativeColumn(4);
                            columns.ConstantColumn(80);
                            columns.ConstantColumn(100);
                        });

                        table.Header(header =>
                        {
                            static IContainer HeaderCell(IContainer c) =>
                                c.Background("#8B0D1D").Padding(8);

                            HeaderCell(header.Cell()).Text("Lp.").FontColor(Colors.White).Bold().FontSize(9);
                            HeaderCell(header.Cell()).Text("Opis usługi").FontColor(Colors.White).Bold().FontSize(9);
                            HeaderCell(header.Cell()).Text("Data").FontColor(Colors.White).Bold().FontSize(9);
                            HeaderCell(header.Cell()).AlignRight().Text("Kwota brutto").FontColor(Colors.White).Bold().FontSize(9);
                        });

                        var idx = 1;
                        foreach (var p in invoice.Payments)
                        {
                            var bg = idx % 2 == 0 ? light : Colors.White;
                            table.Cell().Background(bg).BorderBottom(1).BorderColor(border).Padding(8).Text(idx.ToString()).FontSize(9);
                            table.Cell().Background(bg).BorderBottom(1).BorderColor(border).Padding(8).Text(p.ServiceDescription).FontSize(9);
                            table.Cell().Background(bg).BorderBottom(1).BorderColor(border).Padding(8).Text(p.PaidAt?.ToString("dd.MM.yyyy") ?? "–").FontSize(9);
                            table.Cell().Background(bg).BorderBottom(1).BorderColor(border).Padding(8).AlignRight().Text($"{p.Amount:0.00} PLN").FontSize(9).Bold();
                            idx++;
                        }
                    });

                    // ── Summary ───────────────────────────────────────────────
                    col.Item().PaddingHorizontal(28).PaddingTop(16).AlignRight().Width(220).Column(c =>
                    {
                        c.Item().Background(light).Border(1).BorderColor(border).Padding(12).Column(inner =>
                        {
                            inner.Item().Row(r =>
                            {
                                r.RelativeItem().Text("Wartość netto:").FontSize(9).FontColor(muted);
                                r.ConstantItem(100).AlignRight().Text($"{invoice.NetAmount:0.00} PLN").FontSize(9);
                            });
                            inner.Item().PaddingTop(4).Row(r =>
                            {
                                r.RelativeItem().Text("VAT 23%:").FontSize(9).FontColor(muted);
                                r.ConstantItem(100).AlignRight().Text($"{invoice.VatAmount:0.00} PLN").FontSize(9);
                            });
                            inner.Item().PaddingTop(8).LineHorizontal(1).LineColor(brand);
                            inner.Item().PaddingTop(8).Row(r =>
                            {
                                r.RelativeItem().Text("RAZEM DO ZAPŁATY:").Bold().FontSize(11).FontColor(brand);
                                r.ConstantItem(100).AlignRight().Text($"{invoice.TotalAmount:0.00} PLN").Bold().FontSize(13).FontColor(brand);
                            });
                        });
                    });

                    // ── Footer ────────────────────────────────────────────────
                    col.Item().PaddingTop(30).PaddingHorizontal(28).LineHorizontal(1).LineColor(border);
                    col.Item().PaddingHorizontal(28).PaddingTop(8).PaddingBottom(28).Row(row =>
                    {
                        row.RelativeItem().Text("Dokument wygenerowany automatycznie przez system CARIZO · carizo.eu")
                            .FontSize(8).FontColor(muted);
                        row.AutoItem().Text(invoice.InvoiceNumber)
                            .FontSize(8).FontColor(muted).AlignRight();
                    });
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
        var html = BuildInvoiceHtml(invoice);
        var adminEmail = _config["Admin:Email"] ?? "kontakt@carizo.eu";

        try
        {
            await _email.SendAsync(user.Email,
                $"Faktura zbiorcza CARIZO – {invoice.InvoiceNumber}", html);

            await _email.SendAsync(adminEmail,
                $"[KOPIA] Faktura {invoice.InvoiceNumber} – {user.Email}", html);

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

    private string BuildInvoiceHtml(Invoice inv)
    {
        var ci = new System.Globalization.CultureInfo("pl-PL");
        var monthName = ci.DateTimeFormat.GetMonthName(inv.Month);
        var user = inv.User;

        // Determine buyer display data — prefer payment billing snapshot, fall back to user profile
        var firstPayment = inv.Payments.FirstOrDefault();
        var buyerName = !string.IsNullOrWhiteSpace(firstPayment?.BillingName)
            ? firstPayment.BillingName
            : (user?.AccountType == AccountType.Business && !string.IsNullOrWhiteSpace(user.CompanyName)
                ? user.CompanyName
                : $"{user?.Name} {user?.Surname}");

        var buyerNip = !string.IsNullOrWhiteSpace(firstPayment?.BillingNip)
            ? firstPayment.BillingNip
            : user?.Nip;

        var buyerAddress = (!string.IsNullOrWhiteSpace(firstPayment?.BillingStreet) || !string.IsNullOrWhiteSpace(firstPayment?.BillingCity))
            ? $"{firstPayment?.BillingStreet}, {firstPayment?.BillingPostalCode} {firstPayment?.BillingCity}"
            : null;

        var sName    = _config["Invoice:SellerName"]    ?? "CARIZO Wiktor Niezgoda";
        var sNip     = _config["Invoice:SellerNip"]     ?? "9452331007";
        var sRegon   = _config["Invoice:SellerRegon"]   ?? "544870688";
        var sAddress = _config["Invoice:SellerAddress"] ?? "ul. Henryka Pachońskiego 7/60, 31-223 Kraków";

        var rows = new StringBuilder();
        var idx = 1;
        foreach (var p in inv.Payments)
        {
            var bg = idx % 2 == 0 ? "#f9f9f9" : "#ffffff";
            rows.Append($"<tr style=\"background:{bg}\">" +
                $"<td style=\"padding:9px 12px;border-bottom:1px solid #eee;font-size:13px\">{idx++}</td>" +
                $"<td style=\"padding:9px 12px;border-bottom:1px solid #eee;font-size:13px\">{p.ServiceDescription}</td>" +
                $"<td style=\"padding:9px 12px;border-bottom:1px solid #eee;font-size:13px\">{p.PaidAt?.ToString("dd.MM.yyyy") ?? "–"}</td>" +
                $"<td style=\"padding:9px 12px;border-bottom:1px solid #eee;font-size:13px;text-align:right;font-weight:600\">{p.Amount:0.00} PLN</td>" +
                "</tr>");
        }

        var nipRow    = !string.IsNullOrWhiteSpace(buyerNip)     ? $"<div style=\"color:#555\">NIP: {buyerNip}</div>" : "";
        var addrRow   = !string.IsNullOrWhiteSpace(buyerAddress) ? $"<div style=\"color:#555\">{buyerAddress}</div>" : "";

        return $@"<!DOCTYPE html>
<html><head><meta charset=""UTF-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1""></head>
<body style=""margin:0;padding:0;background:#f0f0f0;font-family:Arial,sans-serif"">
<table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f0f0f0;padding:32px 0"">
<tr><td align=""center"">
<table width=""680"" cellpadding=""0"" cellspacing=""0"" style=""background:#ffffff;border-radius:4px;overflow:hidden;box-shadow:0 2px 12px rgba(0,0,0,.1)"">

  <!-- Header -->
  <tr>
    <td style=""background:#8B0D1D;padding:28px 32px"">
      <table width=""100%"" cellpadding=""0"" cellspacing=""0"">
        <tr>
          <td>
            <img src=""https://carizo.eu/carizo-logo.svg"" alt=""CARIZO"" height=""40"" style=""display:block;height:40px"" />
            <div style=""font-size:11px;color:#e8a0a8;margin-top:6px"">platforma motoryzacyjna · carizo.eu</div>
          </td>
          <td align=""right"">
            <div style=""font-size:15px;font-weight:700;color:#fff"">FAKTURA ZBIORCZA</div>
            <div style=""font-size:12px;color:#e8a0a8;margin-top:4px"">Nr {inv.InvoiceNumber}</div>
          </td>
        </tr>
      </table>
    </td>
  </tr>

  <!-- Meta strip -->
  <tr>
    <td style=""background:#f7f7f7;padding:14px 32px;border-bottom:1px solid #e8e8e8"">
      <table width=""100%"" cellpadding=""0"" cellspacing=""0"">
        <tr>
          <td style=""width:33%"">
            <div style=""font-size:10px;color:#999;text-transform:uppercase;letter-spacing:.5px"">Okres rozliczeniowy</div>
            <div style=""font-size:13px;font-weight:700;color:#1a1a1a;margin-top:2px"">{monthName} {inv.Year}</div>
          </td>
          <td style=""width:33%"">
            <div style=""font-size:10px;color:#999;text-transform:uppercase;letter-spacing:.5px"">Data wystawienia</div>
            <div style=""font-size:13px;font-weight:700;color:#1a1a1a;margin-top:2px"">{inv.GeneratedAt:dd.MM.yyyy}</div>
          </td>
          <td style=""width:33%"">
            <div style=""font-size:10px;color:#999;text-transform:uppercase;letter-spacing:.5px"">Forma płatności</div>
            <div style=""font-size:13px;font-weight:700;color:#1a1a1a;margin-top:2px"">Płatność elektroniczna</div>
          </td>
        </tr>
      </table>
    </td>
  </tr>

  <!-- Parties -->
  <tr>
    <td style=""padding:24px 32px"">
      <table width=""100%"" cellpadding=""0"" cellspacing=""0"">
        <tr>
          <td width=""48%"" valign=""top"" style=""border:1px solid #e0e0e0;border-radius:3px;padding:16px"">
            <div style=""font-size:10px;font-weight:700;color:#8B0D1D;text-transform:uppercase;letter-spacing:.5px;margin-bottom:8px"">Sprzedawca</div>
            <div style=""font-size:14px;font-weight:700;color:#1a1a1a"">{sName}</div>
            <div style=""color:#666;margin-top:4px;font-size:12px"">NIP: {sNip}</div>
            <div style=""color:#666;font-size:12px"">REGON: {sRegon}</div>
            <div style=""color:#333;margin-top:6px;font-size:12px"">{sAddress}</div>
          </td>
          <td width=""4%""></td>
          <td width=""48%"" valign=""top"" style=""border:2px solid #8B0D1D;border-radius:3px;padding:16px"">
            <div style=""font-size:10px;font-weight:700;color:#8B0D1D;text-transform:uppercase;letter-spacing:.5px;margin-bottom:8px"">Nabywca</div>
            <div style=""font-size:14px;font-weight:700;color:#1a1a1a"">{buyerName}</div>
            {nipRow}
            {addrRow}
            <div style=""color:#888;margin-top:6px;font-size:12px"">{user?.Email}</div>
          </td>
        </tr>
      </table>
    </td>
  </tr>

  <!-- Items table -->
  <tr>
    <td style=""padding:0 32px 24px"">
      <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""border-collapse:collapse"">
        <thead>
          <tr style=""background:#8B0D1D"">
            <th style=""padding:10px 12px;color:#fff;font-size:11px;text-align:left;width:36px"">Lp.</th>
            <th style=""padding:10px 12px;color:#fff;font-size:11px;text-align:left"">Opis usługi</th>
            <th style=""padding:10px 12px;color:#fff;font-size:11px;text-align:left;width:90px"">Data</th>
            <th style=""padding:10px 12px;color:#fff;font-size:11px;text-align:right;width:110px"">Kwota brutto</th>
          </tr>
        </thead>
        <tbody>
          {rows}
        </tbody>
      </table>
    </td>
  </tr>

  <!-- Totals -->
  <tr>
    <td style=""padding:0 32px 32px"" align=""right"">
      <table cellpadding=""0"" cellspacing=""0"" style=""border:1px solid #e0e0e0;border-radius:3px;background:#f7f7f7;min-width:240px"">
        <tr>
          <td style=""padding:12px 20px 4px;font-size:12px;color:#666"">Wartość netto:</td>
          <td style=""padding:12px 20px 4px;font-size:12px;text-align:right"">{inv.NetAmount:0.00} PLN</td>
        </tr>
        <tr>
          <td style=""padding:4px 20px 12px;font-size:12px;color:#666"">VAT 23%:</td>
          <td style=""padding:4px 20px 12px;font-size:12px;text-align:right"">{inv.VatAmount:0.00} PLN</td>
        </tr>
        <tr>
          <td colspan=""2"" style=""padding:0 20px""><div style=""border-top:2px solid #8B0D1D""></div></td>
        </tr>
        <tr>
          <td style=""padding:12px 20px;font-size:13px;font-weight:700;color:#8B0D1D"">RAZEM DO ZAPŁATY:</td>
          <td style=""padding:12px 20px;font-size:16px;font-weight:700;color:#8B0D1D;text-align:right"">{inv.TotalAmount:0.00} PLN</td>
        </tr>
      </table>
    </td>
  </tr>

  <!-- Footer -->
  <tr>
    <td style=""background:#f7f7f7;border-top:1px solid #e8e8e8;padding:16px 32px"">
      <table width=""100%"" cellpadding=""0"" cellspacing=""0"">
        <tr>
          <td style=""font-size:11px;color:#aaa"">Dokument wygenerowany automatycznie przez system CARIZO · carizo.eu</td>
          <td align=""right"" style=""font-size:11px;color:#aaa"">{inv.InvoiceNumber}</td>
        </tr>
      </table>
    </td>
  </tr>

</table>
</td></tr>
</table>
</body></html>";
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
