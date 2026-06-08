using System.Net;
using System.Net.Mail;
using cars_website_api.CarsWebsite.Interfaces;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        var section = _config.GetSection("Smtp");
        var host = section["Host"];
        if (string.IsNullOrEmpty(host))
        {
            _logger.LogWarning("SMTP nie skonfigurowany – pominięto e-mail do {To}", to);
            return;
        }

        var port = int.TryParse(section["Port"], out var p) ? p : 587;
        var from = section["From"] ?? "powiadomienia@carizo.pl";

        try
        {
            using var client = new SmtpClient(host, port)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(section["User"], section["Password"])
            };
            await client.SendMailAsync(
                new MailMessage(from, to, subject, htmlBody) { IsBodyHtml = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd wysyłki e-mail do {To}", to);
        }
    }

    public static string BuildHtml(
        string title,
        string mainText,
        string? detailsHtml = null,
        string? ctaUrl = null,
        string? ctaLabel = null)
    {
        var details = detailsHtml != null
            ? $"<div class=\"highlight\">{detailsHtml}</div>"
            : string.Empty;

        var cta = ctaUrl != null
            ? $"<p style=\"margin-top:20px\"><a href=\"{ctaUrl}\" class=\"btn\">{ctaLabel ?? "Przejdź"}</a></p>"
            : string.Empty;

        return $@"<!DOCTYPE html>
<html><head><meta charset=""UTF-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<style>
*{{box-sizing:border-box;margin:0;padding:0}}
body{{font-family:Arial,sans-serif;background:#0a0a0a;color:#e0e0e0;padding:20px}}
.wrap{{max-width:580px;margin:0 auto;background:#111;border:1px solid #1e1e1e;border-radius:10px;overflow:hidden}}
.hdr{{background:#0a0a0a;padding:20px 28px;border-bottom:1px solid #1e1e1e}}
.logo{{font-size:20px;font-weight:900;color:#fff;letter-spacing:-0.5px}}
.logo span{{color:#e53935}}
.body{{padding:28px}}
.title{{font-size:19px;font-weight:700;color:#fff;margin-bottom:12px}}
.text{{font-size:14px;color:#aaa;line-height:1.65}}
.highlight{{background:#171717;border:1px solid #1e1e1e;border-radius:7px;padding:14px 16px;margin:16px 0}}
.highlight p{{font-size:13px;color:#ccc;margin:3px 0}}
.highlight strong{{color:#fff}}
.btn{{display:inline-block;background:#e53935;color:#fff;padding:11px 22px;border-radius:6px;text-decoration:none;font-weight:600;font-size:13px}}
.ftr{{background:#0a0a0a;border-top:1px solid #1e1e1e;padding:14px 28px;font-size:11px;color:#444;line-height:1.6}}
a.ftr-link{{color:#e53935;text-decoration:none}}
</style></head>
<body>
<div class=""wrap"">
  <div class=""hdr""><div class=""logo"">CARI<span>ZO</span></div></div>
  <div class=""body"">
    <p class=""title"">{title}</p>
    <p class=""text"">{mainText}</p>
    {details}
    {cta}
  </div>
  <div class=""ftr"">
    Ta wiadomość została wysłana automatycznie przez system CARIZO · carizo.pl<br>
    Możesz zarządzać powiadomieniami w <a href=""https://carizo.pl/dashboard"" class=""ftr-link"">ustawieniach konta</a>.
  </div>
</div>
</body></html>";
    }
}
