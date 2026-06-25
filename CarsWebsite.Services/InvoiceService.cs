using System.Text;
using CarsWebsite;
using cars_website_api.CarsWebsite.DTOs.Invoice;
using cars_website_api.CarsWebsite.DTOs.Payment;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

public class InvoiceService : IInvoiceService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly INotificationService _notifications;
    private readonly IEmailService _email;
    private readonly IKSeFService _ksef;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(AppDbContext context, IConfiguration config, INotificationService notifications, IEmailService email, IKSeFService ksef, ILogger<InvoiceService> logger)
    {
        _context = context;
        _config = config;
        _notifications = notifications;
        _email = email;
        _ksef = ksef;
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

                var ksefRef = await _ksef.SendInvoiceAsync(invoice, groupPayments);
                if (ksefRef != null)
                {
                    invoice.KSeFReferenceNumber = ksefRef;
                    invoice.IsKSeFSent = true;
                    await _context.SaveChangesAsync();
                }

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

        return GenerateInvoicePdfFromModel(invoice);
    }

    private byte[] GenerateInvoicePdfFromModel(Invoice invoice)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var user = invoice.User;
        var firstPayment = invoice.Payments.FirstOrDefault();

        var buyerName = !string.IsNullOrWhiteSpace(firstPayment?.BillingName)
            ? firstPayment.BillingName
            : (user?.AccountType == AccountType.Business && !string.IsNullOrWhiteSpace(user.CompanyName)
                ? user.CompanyName : $"{user?.Name} {user?.Surname}".Trim());
        var buyerNip     = (firstPayment?.BillingNip ?? user?.Nip) ?? "";
        var buyerStreet  = firstPayment?.BillingStreet ?? "";
        var buyerPostal  = firstPayment?.BillingPostalCode ?? "";
        var buyerCity    = firstPayment?.BillingCity ?? "";
        var buyerEmail   = user?.Email ?? "";

        var sellerName    = _config["Invoice:SellerName"]    ?? "CARIZO Wiktor Niezgoda";
        var sellerNip     = _config["Invoice:SellerNip"]     ?? "9452331007";
        var sellerRegon   = _config["Invoice:SellerRegon"]   ?? "544870688";
        var sellerPhone   = _config["Invoice:SellerPhone"]   ?? "+48 531 657 872";
        var sellerEmail   = _config["Invoice:SellerEmail"]   ?? "kontakt@carizo.eu";
        var sellerStreet  = _config["Invoice:SellerStreet"]  ?? "ul. Henryka Pachońskiego 7/60";
        var sellerCity    = _config["Invoice:SellerCity"]    ?? "31-223 Kraków";
        var accountNumber = _config["Invoice:AccountNumber"] ?? "29 1050 1445 1000 0090 8697 1745";

        // Parse invoice number parts e.g. FZ/2026/06/0001
        var parts = invoice.InvoiceNumber.Split('/');
        var numYear = parts.Length > 1 ? parts[1] : "";
        var numRest = parts.Length > 1 ? "/" + string.Join("/", parts.Skip(2)) : "";

        var issueDate   = invoice.GeneratedAt.ToString("dd.MM.yyyy");
        var saleDate    = new DateTime(invoice.Year, invoice.Month, 1).ToString("dd.MM.yyyy");
        var termDate    = invoice.GeneratedAt.ToString("dd.MM.yyyy");
        var orderNum    = firstPayment?.ImojeOrderId ?? "–";
        var paymentNum  = firstPayment?.ImojeTransactionId ?? "–";

        // QR code pointing to invoice verification
        var qrBytes = GenerateQrBytes($"https://carizo.eu/faktury/{invoice.InvoiceNumber}");

        const string dark   = "#111111";
        const string brand  = "#C0392B";
        const string muted  = "#888888";
        const string border = "#e0e0e0";
        const string light  = "#f5f5f5";
        const string white  = "#ffffff";
        const string hdr    = "#1a1a1a";

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(0);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial").FontColor(dark));

                page.Content().Column(col =>
                {
                    // ── Dark header ───────────────────────────────────────────
                    col.Item().Background(hdr).Padding(32).Column(hc =>
                    {
                        hc.Item().Row(row =>
                        {
                            // Left: logo + contacts
                            row.RelativeItem().Column(lc =>
                            {
                                lc.Item().Text("CARIZO")
                                    .FontSize(38).Bold().FontColor(white).LetterSpacing(0.05f);
                                lc.Item().PaddingTop(18).Column(contacts =>
                                {
                                    ContactRow(contacts, "carizo.eu");
                                    ContactRow(contacts, sellerEmail);
                                    ContactRow(contacts, sellerPhone);
                                    ContactRow(contacts, sellerStreet);
                                    ContactRow(contacts, sellerCity);
                                });
                            });

                            // Right: FAKTURA ZBIORCZA + number
                            row.RelativeItem().AlignRight().Column(rc =>
                            {
                                rc.Item().AlignRight().Text("FAKTURA ZBIORCZA")
                                    .FontSize(10).FontColor(muted).Bold().LetterSpacing(0.08f);
                                rc.Item().PaddingTop(6).AlignRight().Text(t =>
                                {
                                    t.Span("FZ/").FontSize(30).Bold().FontColor(white);
                                    t.Span(numYear).FontSize(30).Bold().FontColor(brand);
                                    t.Span(numRest).FontSize(30).Bold().FontColor(white);
                                });

                                // Date grid
                                rc.Item().PaddingTop(20).AlignRight().Table(tbl =>
                                {
                                    tbl.ColumnsDefinition(c =>
                                    {
                                        c.RelativeColumn();
                                        c.RelativeColumn();
                                    });
                                    DateRow(tbl, "DATA WYSTAWIENIA:", issueDate);
                                    DateRow(tbl, "DATA SPRZEDAŻY:", saleDate);
                                    DateRow(tbl, "TERMIN PŁATNOŚCI:", termDate);
                                    DateRow(tbl, "SPOSÓB PŁATNOŚCI:", "Płatność elektroniczna");
                                    DateRow(tbl, "STATUS PŁATNOŚCI:", invoice.SentAt.HasValue ? "Opłacono" : "Oczekuje");
                                });
                            });
                        });
                    });

                    // ── Parties ───────────────────────────────────────────────
                    col.Item().PaddingHorizontal(28).PaddingTop(20).Row(row =>
                    {
                        // Seller
                        row.RelativeItem().Border(1).BorderColor(border).Padding(16).Column(c =>
                        {
                            c.Item().Text("SPRZEDAWCA").FontSize(7).Bold().FontColor(brand).LetterSpacing(0.1f);
                            c.Item().PaddingTop(6).Text(sellerName).Bold().FontSize(11).FontColor(dark);
                            c.Item().PaddingTop(6).Text($"NIP: {sellerNip}").FontSize(9).FontColor(muted);
                            c.Item().Text($"REGON: {sellerRegon}").FontSize(9).FontColor(muted);
                            c.Item().PaddingTop(4).Text(sellerStreet).FontSize(9);
                            c.Item().Text(sellerCity).FontSize(9);
                            c.Item().Text("Polska").FontSize(9).FontColor(muted);
                        });

                        row.ConstantItem(14);

                        // Buyer
                        row.RelativeItem().Border(1).BorderColor(border).BorderLeft(3).BorderColor(brand).Padding(16).Column(c =>
                        {
                            c.Item().Text("NABYWCA").FontSize(7).Bold().FontColor(brand).LetterSpacing(0.1f);
                            c.Item().PaddingTop(6).Text(buyerName).Bold().FontSize(11).FontColor(dark);
                            if (!string.IsNullOrWhiteSpace(buyerNip))
                                c.Item().PaddingTop(6).Text($"NIP: {buyerNip}").FontSize(9).FontColor(muted);
                            if (!string.IsNullOrWhiteSpace(buyerStreet))
                                c.Item().PaddingTop(4).Text(buyerStreet).FontSize(9);
                            if (!string.IsNullOrWhiteSpace(buyerCity))
                                c.Item().Text($"{buyerPostal} {buyerCity}".Trim()).FontSize(9);
                            c.Item().Text("Polska").FontSize(9).FontColor(muted);
                            if (!string.IsNullOrWhiteSpace(buyerEmail))
                                c.Item().PaddingTop(4).Text(buyerEmail).FontSize(9).FontColor(muted);
                        });
                    });

                    // ── Items table ───────────────────────────────────────────
                    col.Item().PaddingHorizontal(28).PaddingTop(20).Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(24);   // Lp.
                            cols.RelativeColumn(3);    // Nazwa usługi
                            cols.ConstantColumn(44);   // Okres
                            cols.ConstantColumn(68);   // Data
                            cols.ConstantColumn(72);   // Kwota netto
                            cols.ConstantColumn(58);   // VAT 23%
                            cols.ConstantColumn(80);   // Kwota brutto
                        });

                        table.Header(header =>
                        {
                            IContainer H(IContainer c) => c.Background(dark).PaddingVertical(9).PaddingHorizontal(6);
                            H(header.Cell()).Text("LP.").FontColor(white).Bold().FontSize(8);
                            H(header.Cell()).Text("NAZWA USŁUGI").FontColor(white).Bold().FontSize(8);
                            H(header.Cell()).AlignCenter().Text("OKRES").FontColor(white).Bold().FontSize(8);
                            H(header.Cell()).AlignCenter().Text("DATA").FontColor(white).Bold().FontSize(8);
                            H(header.Cell()).AlignRight().Text("KWOTA NETTO").FontColor(white).Bold().FontSize(8);
                            H(header.Cell()).AlignRight().Text("VAT 23%").FontColor(white).Bold().FontSize(8);
                            H(header.Cell()).AlignRight().Text("KWOTA BRUTTO").FontColor(white).Bold().FontSize(8);
                        });

                        var idx = 1;
                        foreach (var p in invoice.Payments)
                        {
                            var bg = idx % 2 == 0 ? light : white;
                            var net = Math.Round(p.Amount / 1.23m, 2);
                            var vat = Math.Round(p.Amount - net, 2);
                            var dur = p.DurationDays.HasValue ? $"{p.DurationDays} dni" : "–";

                            IContainer D(IContainer c) => c.Background(bg).BorderBottom(1).BorderColor(border).PaddingVertical(9).PaddingHorizontal(6);
                            D(table.Cell()).Text(idx.ToString()).FontSize(8).FontColor(muted);
                            D(table.Cell()).Text(p.ServiceDescription).FontSize(8);
                            D(table.Cell()).AlignCenter().Text(dur).FontSize(8).FontColor(muted);
                            D(table.Cell()).AlignCenter().Text(p.PaidAt?.ToString("dd.MM.yyyy") ?? "–").FontSize(8).FontColor(muted);
                            D(table.Cell()).AlignRight().Text($"{net:0.00} PLN").FontSize(8);
                            D(table.Cell()).AlignRight().Text($"{vat:0.00} PLN").FontSize(8);
                            D(table.Cell()).AlignRight().Text($"{p.Amount:0.00} PLN").FontSize(8).Bold().FontColor(brand);
                            idx++;
                        }
                    });

                    // ── Bottom: extra info + totals ───────────────────────────
                    col.Item().PaddingHorizontal(28).PaddingTop(20).Row(row =>
                    {
                        // Left: Dodatkowe informacje
                        row.RelativeItem().Column(lc =>
                        {
                            lc.Item().Text("DODATKOWE INFORMACJE").FontSize(7).Bold().FontColor(brand).LetterSpacing(0.08f);
                            lc.Item().PaddingTop(10).Table(tbl =>
                            {
                                tbl.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                                ExtraRow(tbl, "Numer zamówienia:", orderNum);
                                ExtraRow(tbl, "Numer płatności ING:", paymentNum);
                                ExtraRow(tbl, "Metoda płatności:", "Bramka ING");
                                ExtraRow(tbl, "Data płatności:", firstPayment?.PaidAt?.ToString("dd.MM.yyyy") ?? "–");
                                ExtraRow(tbl, "Waluta:", "PLN");
                            });
                        });

                        row.ConstantItem(20);

                        // Right: totals
                        row.RelativeItem().Column(rc =>
                        {
                            rc.Item().Border(1).BorderColor(border).Table(tbl =>
                            {
                                tbl.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(100); });

                                TotalRow(tbl, "WARTOŚĆ NETTO", $"{invoice.NetAmount:0.00} PLN", light, dark, false);
                                TotalRow(tbl, "VAT 23%", $"{invoice.VatAmount:0.00} PLN", white, dark, false);
                                TotalRow(tbl, "DO ZAPŁATY", $"{invoice.TotalAmount:0.00} PLN", white, brand, true);
                            });
                        });
                    });

                    // ── QR + thank you ────────────────────────────────────────
                    col.Item().PaddingHorizontal(28).PaddingTop(20).LineHorizontal(1).LineColor(border);
                    col.Item().PaddingHorizontal(28).PaddingTop(14).PaddingBottom(16).Row(row =>
                    {
                        // QR code
                        row.ConstantItem(70).Column(qc =>
                        {
                            if (qrBytes != null)
                                qc.Item().Width(60).Height(60).Image(qrBytes);
                            qc.Item().PaddingTop(4).Text("ZESKANUJ KOD QR")
                                .FontSize(6).Bold().FontColor(brand).LetterSpacing(0.05f);
                            qc.Item().Text("aby zweryfikować")
                                .FontSize(6).FontColor(muted);
                            qc.Item().Text("autentyczność faktury")
                                .FontSize(6).FontColor(muted);
                        });

                        row.RelativeItem();

                        // Thank you
                        row.AutoItem().AlignRight().Column(tc =>
                        {
                            tc.Item().AlignRight().Text("Dziękujemy za zaufanie i współpracę.")
                                .FontSize(9).FontColor(muted);
                            tc.Item().AlignRight().Text("Zespół CARIZO")
                                .FontSize(10).Bold().FontColor(brand);
                        });
                    });

                    // ── Dark footer ───────────────────────────────────────────
                    col.Item().Background(hdr).PaddingHorizontal(28).PaddingVertical(18).Row(row =>
                    {
                        // Logo + address
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("CARIZO").FontSize(14).Bold().FontColor(white).LetterSpacing(0.05f);
                            c.Item().PaddingTop(4).Text(sellerName).FontSize(8).FontColor(muted);
                            c.Item().Text($"{sellerStreet}, {sellerCity}, Polska").FontSize(8).FontColor(muted);
                        });

                        // NIP / REGON
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text($"NIP: {sellerNip}").FontSize(8).FontColor(muted);
                            c.Item().PaddingTop(2).Text($"REGON: {sellerRegon}").FontSize(8).FontColor(muted);
                        });

                        // Account
                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text("Numer konta:").FontSize(7).FontColor(muted);
                            c.Item().AlignRight().Text(accountNumber).FontSize(8).FontColor(white).Bold();
                        });
                    });

                    // ── Legal note ────────────────────────────────────────────
                    col.Item().Background("#0a0a0a").PaddingHorizontal(28).PaddingVertical(8).Column(c =>
                    {
                        c.Item().AlignCenter().Text("Faktura wygenerowana automatycznie przez system CARIZO.  Dokument nie wymaga podpisu.")
                            .FontSize(7).FontColor("#555555");
                    });
                });
            });
        }).GeneratePdf();
    }

    private static void ContactRow(ColumnDescriptor col, string text) =>
        col.Item().Text(text).FontSize(8).FontColor("#aaaaaa");

    private static void DateRow(TableDescriptor tbl, string label, string value)
    {
        tbl.Cell().PaddingVertical(2).AlignRight().Text(label).FontSize(7).FontColor("#888888");
        tbl.Cell().PaddingVertical(2).PaddingLeft(8).Text(value).FontSize(7).FontColor("#ffffff").Bold();
    }

    private static void ExtraRow(TableDescriptor tbl, string label, string value)
    {
        tbl.Cell().PaddingVertical(3).Text(label).FontSize(8).FontColor("#888888");
        tbl.Cell().PaddingVertical(3).Text(value).FontSize(8);
    }

    private static void TotalRow(TableDescriptor tbl, string label, string value, string bg, string color, bool large)
    {
        var fs = large ? 12 : 9;
        tbl.Cell().Background(bg).PaddingVertical(large ? 12 : 8).PaddingHorizontal(10)
            .Text(label).FontSize(fs).Bold().FontColor(color);
        tbl.Cell().Background(bg).PaddingVertical(large ? 12 : 8).PaddingHorizontal(10)
            .AlignRight().Text(value).FontSize(fs).Bold().FontColor(color);
    }

    private static byte[]? GenerateQrBytes(string content)
    {
        try
        {
            using var gen = new QRCodeGenerator();
            using var data = gen.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
            using var qr = new PngByteQRCode(data);
            return qr.GetGraphic(5);
        }
        catch { return null; }
    }

    public Task<byte[]> GenerateTestPdfAsync()
    {
        var fakeInvoice = new Invoice
        {
            Id = 0,
            InvoiceNumber = "FZ/2026/06/0001",
            Month = 6,
            Year = 2026,
            TotalAmount = 447.00m,
            NetAmount = 363.41m,
            VatAmount = 83.59m,
            VatRate = 0.23m,
            Status = InvoiceStatus.Sent,
            GeneratedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            SentAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            User = new User
            {
                Name = "Auto Komis",
                Surname = "Ostrowiec Sp. z o.o.",
                Email = "kontakt@autokomis.pl",
                AccountType = AccountType.Business,
                CompanyName = "Auto Komis Ostrowiec Sp. z o.o.",
                Nip = "6612345678"
            },
            Payments = new List<Payment>
            {
                new() {
                    ServiceDescription = "Pakiet Premium – wyróżnienie ogłoszenia (30 dni)",
                    Amount = 199.00m, DurationDays = 30,
                    PaidAt = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc),
                    ImojeOrderId = "ZAM/2026/06/0150",
                    ImojeTransactionId = "ING/2026/06/0150",
                    BillingNip = "6612345678",
                    BillingName = "Auto Komis Ostrowiec Sp. z o.o.",
                    BillingStreet = "ul. Przykładowa 15",
                    BillingPostalCode = "27-400",
                    BillingCity = "Ostrowiec Świętokrzyski"
                },
                new() {
                    ServiceDescription = "Pakiet TOP – podbicie ogłoszenia na górę listy (7 dni)",
                    Amount = 49.00m, DurationDays = 7,
                    PaidAt = new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc),
                    ImojeOrderId = "ZAM/2026/06/0151",
                    ImojeTransactionId = "ING/2026/06/0151",
                },
                new() {
                    ServiceDescription = "Pakiet Premium – wyróżnienie ogłoszenia (30 dni)",
                    Amount = 199.00m, DurationDays = 30,
                    PaidAt = new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc),
                    ImojeOrderId = "ZAM/2026/06/0152",
                    ImojeTransactionId = "ING/2026/06/0152",
                }
            }
        };

        var bytes = GenerateInvoicePdfFromModel(fakeInvoice);
        return Task.FromResult(bytes);
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
            var bg = idx % 2 == 0 ? "#fafafa" : "#ffffff";
            rows.Append($"<tr style=\"background:{bg}\">" +
                $"<td style=\"padding:11px 14px;border-bottom:1px solid #f0f0f0;font-size:13px;color:#777\">{idx++}</td>" +
                $"<td style=\"padding:11px 14px;border-bottom:1px solid #f0f0f0;font-size:13px;color:#222\">{p.ServiceDescription}</td>" +
                $"<td style=\"padding:11px 14px;border-bottom:1px solid #f0f0f0;font-size:12px;color:#666\">{p.PaidAt?.ToString("dd.MM.yyyy") ?? "–"}</td>" +
                $"<td style=\"padding:11px 14px;border-bottom:1px solid #f0f0f0;font-size:13px;font-weight:700;text-align:right;color:#222\">{p.Amount:0.00} PLN</td>" +
                "</tr>");
        }

        var nipRow    = !string.IsNullOrWhiteSpace(buyerNip)     ? $"<div style=\"color:#555\">NIP: {buyerNip}</div>" : "";
        var addrRow   = !string.IsNullOrWhiteSpace(buyerAddress) ? $"<div style=\"color:#555\">{buyerAddress}</div>" : "";

        return $@"<!DOCTYPE html>
<html><head><meta charset=""UTF-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1""></head>
<body style=""margin:0;padding:0;background:#ececec;font-family:'Helvetica Neue',Arial,sans-serif"">
<table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#ececec;padding:40px 0"">
<tr><td align=""center"">
<table width=""660"" cellpadding=""0"" cellspacing=""0"" style=""background:#ffffff;border-radius:6px;overflow:hidden;box-shadow:0 4px 24px rgba(0,0,0,.13)"">

  <!-- Header -->
  <tr>
    <td style=""background:#6b0d17;padding:0"">
      <table width=""100%"" cellpadding=""0"" cellspacing=""0"">
        <tr>
          <!-- Left: logo -->
          <td style=""padding:30px 36px 20px;width:50%"" valign=""bottom"">
            <div style=""font-size:34px;font-weight:300;color:#fff;letter-spacing:3px;line-height:1"">CARI<span style=""color:#f5b5be"">Z</span>O</div>
          </td>
          <!-- Right: doc type + number -->
          <td style=""padding:30px 36px 20px;width:50%"" valign=""bottom"" align=""right"">
            <div style=""font-size:11px;color:#f5b5be;letter-spacing:2px;text-transform:uppercase;margin-bottom:4px"">Faktura zbiorcza</div>
            <div style=""font-size:22px;font-weight:700;color:#fff"">{inv.InvoiceNumber}</div>
          </td>
        </tr>
        <!-- Meta bar -->
        <tr>
          <td colspan=""2"" style=""background:rgba(0,0,0,.18);padding:12px 36px"">
            <table width=""100%"" cellpadding=""0"" cellspacing=""0"">
              <tr>
                <td style=""font-size:11px;color:#f5b5be"">
                  <span style=""color:#fff;font-weight:600"">{monthName} {inv.Year}</span>
                  &nbsp;·&nbsp; wystawiono {inv.GeneratedAt:dd.MM.yyyy}
                  &nbsp;·&nbsp; płatność elektroniczna
                </td>
                <td align=""right"" style=""font-size:11px;color:#f5b5be"">carizo.eu</td>
              </tr>
            </table>
          </td>
        </tr>
      </table>
    </td>
  </tr>

  <!-- Parties -->
  <tr>
    <td style=""padding:28px 36px 0"">
      <table width=""100%"" cellpadding=""0"" cellspacing=""0"">
        <tr valign=""top"">
          <!-- Seller -->
          <td width=""47%"" style=""background:#fafafa;border:1px solid #e8e8e8;border-radius:4px;padding:18px 20px"">
            <div style=""font-size:9px;font-weight:700;color:#6b0d17;text-transform:uppercase;letter-spacing:1.5px;margin-bottom:10px"">Sprzedawca</div>
            <div style=""font-size:15px;font-weight:700;color:#111;margin-bottom:6px"">{sName}</div>
            <div style=""font-size:12px;color:#555;line-height:1.7"">
              NIP: {sNip}<br/>
              REGON: {sRegon}<br/>
              {sAddress}
            </div>
          </td>
          <td width=""6%""></td>
          <!-- Buyer -->
          <td width=""47%"" style=""background:#fff8f8;border:2px solid #6b0d17;border-radius:4px;padding:18px 20px"">
            <div style=""font-size:9px;font-weight:700;color:#6b0d17;text-transform:uppercase;letter-spacing:1.5px;margin-bottom:10px"">Nabywca</div>
            <div style=""font-size:15px;font-weight:700;color:#111;margin-bottom:6px"">{buyerName}</div>
            <div style=""font-size:12px;color:#555;line-height:1.7"">
              {(string.IsNullOrWhiteSpace(buyerNip) ? "" : $"NIP: {buyerNip}<br/>")}
              {(string.IsNullOrWhiteSpace(buyerAddress) ? "" : $"{buyerAddress}<br/>")}
              <span style=""color:#999"">{user?.Email}</span>
            </div>
          </td>
        </tr>
      </table>
    </td>
  </tr>

  <!-- Items table -->
  <tr>
    <td style=""padding:24px 36px 0"">
      <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""border-collapse:collapse;border-radius:4px;overflow:hidden"">
        <thead>
          <tr style=""background:#6b0d17"">
            <th style=""padding:11px 14px;color:#fff;font-size:10px;text-align:left;font-weight:600;letter-spacing:.5px;width:40px"">Lp.</th>
            <th style=""padding:11px 14px;color:#fff;font-size:10px;text-align:left;font-weight:600;letter-spacing:.5px"">Opis usługi</th>
            <th style=""padding:11px 14px;color:#fff;font-size:10px;text-align:left;font-weight:600;letter-spacing:.5px;width:88px"">Data</th>
            <th style=""padding:11px 14px;color:#fff;font-size:10px;text-align:right;font-weight:600;letter-spacing:.5px;width:110px"">Kwota brutto</th>
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
    <td style=""padding:20px 36px 32px"" align=""right"">
      <table cellpadding=""0"" cellspacing=""0"" style=""min-width:260px"">
        <tr>
          <td style=""padding:7px 0;font-size:12px;color:#777;border-bottom:1px solid #eee"">Wartość netto</td>
          <td style=""padding:7px 0 7px 24px;font-size:12px;text-align:right;border-bottom:1px solid #eee"">{inv.NetAmount:0.00} PLN</td>
        </tr>
        <tr>
          <td style=""padding:7px 0;font-size:12px;color:#777;border-bottom:1px solid #eee"">VAT 23%</td>
          <td style=""padding:7px 0 7px 24px;font-size:12px;text-align:right;border-bottom:1px solid #eee"">{inv.VatAmount:0.00} PLN</td>
        </tr>
        <tr>
          <td style=""padding:14px 0 0;font-size:14px;font-weight:700;color:#6b0d17"">Razem do zapłaty</td>
          <td style=""padding:14px 0 0 24px;font-size:20px;font-weight:700;color:#6b0d17;text-align:right;white-space:nowrap"">{inv.TotalAmount:0.00} PLN</td>
        </tr>
      </table>
    </td>
  </tr>

  <!-- Footer -->
  <tr>
    <td style=""background:#f5f5f5;border-top:3px solid #6b0d17;padding:14px 36px"">
      <table width=""100%"" cellpadding=""0"" cellspacing=""0"">
        <tr>
          <td style=""font-size:10px;color:#bbb"">Dokument wygenerowany automatycznie przez system CARIZO · carizo.eu</td>
          <td align=""right"" style=""font-size:10px;color:#bbb"">{inv.InvoiceNumber}</td>
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
        KSeFReferenceNumber = inv.KSeFReferenceNumber,
        IsKSeFSent = inv.IsKSeFSent,
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
