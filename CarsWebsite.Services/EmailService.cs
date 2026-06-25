using cars_website_api.CarsWebsite.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

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
        var rawHost = (section["Host"] ?? "").Trim();
        if (string.IsNullOrEmpty(rawHost))
        {
            _logger.LogWarning("SMTP nie skonfigurowany – pominięto e-mail do {To}", to);
            return;
        }

        // Strip accidentally included protocol prefix (e.g. "smtp://smtp.host.com")
        var host = rawHost.Contains("://") ? rawHost.Split("://", 2)[1].TrimEnd('/') : rawHost;
        // Strip accidentally included port suffix (e.g. "smtp.host.com:587")
        if (!host.StartsWith("[") && host.Contains(':'))
            host = host.Split(':')[0];

        var port = int.TryParse(section["Port"], out var p) ? p : 587;
        var from = (section["From"] ?? "powiadomienia@carizo.pl").Trim();
        var user = section["User"];
        var password = section["Password"];

        _logger.LogInformation("[Email] Sending '{Subject}' to {To} via {Host}:{Port}", subject, to, host, port);

        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(from));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = htmlBody };

            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);
            if (!string.IsNullOrEmpty(user))
                await client.AuthenticateAsync(user, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            _logger.LogInformation("[Email] Sent successfully to {To}", to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Email] Błąd wysyłki e-mail do {To} via {Host}:{Port}", to, host, port);
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
