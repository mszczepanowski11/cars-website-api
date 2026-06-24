using cars_website_api.CarsWebsite.DTOs.Financing;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Services;

public class FinancingService : IFinancingService
{
    private readonly AppDbContext _db;
    private readonly IEmailService _email;
    private readonly IConfiguration _config;
    private readonly ILogger<FinancingService> _logger;

    public FinancingService(
        AppDbContext db,
        IEmailService email,
        IConfiguration config,
        ILogger<FinancingService> logger)
    {
        _db = db;
        _email = email;
        _config = config;
        _logger = logger;
    }

    public async Task<int> CreateInquiryAsync(CreateFinancingInquiryDto dto, int? userId)
    {
        var advert = await _db.Adverts
            .Where(a => a.Id == dto.AdvertId && !a.IsHidden)
            .Select(a => new { a.Id, a.Title, a.Price })
            .FirstOrDefaultAsync()
            ?? throw new KeyNotFoundException($"Ogłoszenie {dto.AdvertId} nie zostało znalezione.");

        var type = dto.Type == "credit" ? "credit" : "leasing";

        var inquiry = new FinancingInquiry
        {
            AdvertId       = dto.AdvertId,
            UserId         = userId,
            Name           = dto.Name.Trim(),
            Phone          = dto.Phone.Trim(),
            Email          = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim(),
            Type           = type,
            Price          = dto.Price ?? advert.Price,
            DownPaymentPct = dto.DownPaymentPct,
            Months         = dto.Months,
            Status         = "new",
            CreatedAt      = DateTime.UtcNow,
        };

        _db.FinancingInquiries.Add(inquiry);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "[Financing] Lead #{Id} created: advert={AdvertId} type={Type} name={Name} phone={Phone} userId={UserId}",
            inquiry.Id, inquiry.AdvertId, inquiry.Type, inquiry.Name, inquiry.Phone, userId);

        _ = SendEmailsFireAndForgetAsync(inquiry, advert.Title);

        return inquiry.Id;
    }

    private async Task SendEmailsFireAndForgetAsync(FinancingInquiry inq, string advertTitle)
    {
        var siteUrl    = (_config["Imoje:SiteUrl"] ?? "https://carizo.pl").TrimEnd('/');
        var advertUrl  = $"{siteUrl}/advert/{inq.AdvertId}";
        var typeLabel  = inq.Type == "credit" ? "Kredyt" : "Leasing";
        var adminEmail = _config["Admin:Email"] ?? "kontakt@carizo.eu";

        var detailsHtml =
            $"<p><strong>Ogłoszenie:</strong> {System.Net.WebUtility.HtmlEncode(advertTitle)}</p>" +
            $"<p><strong>Typ finansowania:</strong> {typeLabel}</p>" +
            $"<p><strong>Imię i nazwisko:</strong> {System.Net.WebUtility.HtmlEncode(inq.Name)}</p>" +
            $"<p><strong>Telefon:</strong> {System.Net.WebUtility.HtmlEncode(inq.Phone)}</p>" +
            (inq.Email != null ? $"<p><strong>E-mail:</strong> {System.Net.WebUtility.HtmlEncode(inq.Email)}</p>" : "") +
            (inq.Price.HasValue ? $"<p><strong>Cena pojazdu:</strong> {inq.Price.Value:N0} PLN</p>" : "") +
            (inq.DownPaymentPct.HasValue ? $"<p><strong>Wpłata własna:</strong> {inq.DownPaymentPct}%</p>" : "") +
            (inq.Months.HasValue ? $"<p><strong>Okres finansowania:</strong> {inq.Months} mies.</p>" : "") +
            $"<p><strong>Nr zapytania:</strong> #{inq.Id}</p>";

        // Admin notification
        try
        {
            var adminHtml = EmailService.BuildHtml(
                title: $"Nowe zapytanie o {typeLabel.ToLower()}",
                mainText: $"Użytkownik <strong>{System.Net.WebUtility.HtmlEncode(inq.Name)}</strong> wysłał zapytanie " +
                          $"o {typeLabel.ToLower()} dla ogłoszenia <em>{System.Net.WebUtility.HtmlEncode(advertTitle)}</em>.",
                detailsHtml: detailsHtml,
                ctaUrl: advertUrl,
                ctaLabel: "Zobacz ogłoszenie");

            await _email.SendAsync(adminEmail, $"[CARIZO] Zapytanie o {typeLabel.ToLower()} – {advertTitle}", adminHtml);
            _logger.LogInformation("[Financing] Admin notification sent for lead #{Id}", inq.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Financing] Failed to send admin notification for lead #{Id}", inq.Id);
        }

        // User confirmation email (only when email provided)
        if (!string.IsNullOrEmpty(inq.Email))
        {
            try
            {
                var userHtml = EmailService.BuildHtml(
                    title: "Twoje zapytanie zostało przyjęte",
                    mainText: $"Dziękujemy, <strong>{System.Net.WebUtility.HtmlEncode(inq.Name)}</strong>! " +
                              $"Twoje zapytanie o {typeLabel.ToLower()} dla pojazdu " +
                              $"<em>{System.Net.WebUtility.HtmlEncode(advertTitle)}</em> " +
                              $"zostało zarejestrowane. Skontaktujemy się z Tobą wkrótce.",
                    detailsHtml: detailsHtml,
                    ctaUrl: advertUrl,
                    ctaLabel: "Zobacz ogłoszenie");

                await _email.SendAsync(inq.Email, $"Potwierdzenie zapytania o {typeLabel.ToLower()} – {advertTitle}", userHtml);
                _logger.LogInformation("[Financing] Confirmation email sent for lead #{Id} to {Email}", inq.Id, inq.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Financing] Failed to send confirmation email for lead #{Id}", inq.Id);
            }
        }
    }
}
