using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using cars_website_api.CarsWebsite.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    // Shared HttpClient for the Resend transport (avoids socket exhaustion).
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        var smtp = _config.GetSection("Smtp");
        // From address shared by both transports. Priority: Smtp:From → Smtp:User → fallback.
        var fromCfg = (smtp["From"] ?? "").Trim();
        var from = !string.IsNullOrEmpty(fromCfg) ? fromCfg : (smtp["User"] ?? "kontakt@carizo.eu");

        // Preferred transport: Resend HTTP API over port 443. SMTP egress (25/465/587) is
        // blocked on many PaaS platforms (e.g. Railway), so SMTP ConnectAsync just times out.
        // Read from several name variants so a single/double-underscore or casing slip in the
        // host's env config does not silently disable Resend.
        var resendKey = (
            _config["Resend:ApiKey"]
            ?? _config["RESEND_API_KEY"]
            ?? Environment.GetEnvironmentVariable("Resend__ApiKey")
            ?? Environment.GetEnvironmentVariable("RESEND_API_KEY")
            ?? Environment.GetEnvironmentVariable("RESEND_APIKEY")
            ?? ""
        ).Trim();
        if (!string.IsNullOrEmpty(resendKey))
        {
            await SendViaResendAsync(resendKey, from, to, subject, htmlBody);
            return;
        }

        await SendViaSmtpAsync(smtp, from, to, subject, htmlBody);
    }

    private async Task SendViaResendAsync(string apiKey, string from, string to, string subject, string htmlBody)
    {
        // Resend accepts "Name <addr>" or a bare address. Add a display name if missing.
        var fromHeader = from.Contains('<') ? from : $"CARIZO <{from}>";
        _logger.LogInformation("[Email] Sending '{Subject}' to {To} via Resend HTTP API", subject, to);
        try
        {
            var payload = new
            {
                from = fromHeader,
                to = new[] { to },
                subject,
                html = htmlBody,
                text = HtmlToPlainText(htmlBody),
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode)
                _logger.LogInformation("[Email] Sent successfully to {To} via Resend", to);
            else
                _logger.LogError("[Email] Resend odrzucił e-mail do {To}: {Status} {Body}", to, (int)resp.StatusCode, body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Email] Błąd wysyłki e-mail (Resend) do {To}", to);
        }
    }

    private async Task SendViaSmtpAsync(IConfigurationSection section, string from, string to, string subject, string htmlBody)
    {
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
        var user = section["User"];
        var password = section["Password"];

        _logger.LogInformation("[Email] Sending '{Subject}' to {To} via {Host}:{Port}", subject, to, host, port);

        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(from));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;

            // Reply-To: same as From so replies go to the right place
            message.ReplyTo.Add(MailboxAddress.Parse(from));

            // Multipart/alternative: plain text first (fallback), HTML second (preferred).
            // Having a plain-text part significantly reduces spam score with corporate filters.
            var multipart = new Multipart("alternative");
            multipart.Add(new TextPart("plain") { Text = HtmlToPlainText(htmlBody) });
            multipart.Add(new TextPart("html") { Text = htmlBody });
            message.Body = multipart;

            // Standard headers that improve deliverability
            message.Headers.Add("X-Mailer", "CARIZO Mailer 1.0");

            using var client = new SmtpClient();
            // Fail fast instead of hanging when the SMTP port is blocked (e.g. PaaS egress
            // filtering). Without this, ConnectAsync can block until the OS socket timeout,
            // so neither success nor failure is ever logged.
            client.Timeout = 30_000; // 30s

            // Port 465 → implicit SSL; 587 → STARTTLS; anything else → Auto-detect.
            var secureOptions = port == 465
                ? SecureSocketOptions.SslOnConnect
                : port == 587 ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;

            _logger.LogInformation("[Email] Connecting to {Host}:{Port} ({Tls})", host, port, secureOptions);
            await client.ConnectAsync(host, port, secureOptions);
            _logger.LogInformation("[Email] Connected; authenticating as {User}", user);
            if (!string.IsNullOrEmpty(user))
                await client.AuthenticateAsync(user, password);
            _logger.LogInformation("[Email] Authenticated; sending message");
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            _logger.LogInformation("[Email] Sent successfully to {To}", to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Email] Błąd wysyłki e-mail do {To} via {Host}:{Port}", to, host, port);
        }
    }

    private static string HtmlToPlainText(string html)
    {
        // Replace <br>, <p>, <div> with newlines before stripping tags
        var text = Regex.Replace(html, @"<br\s*/?>|</p>|</div>|</li>|</h[1-6]>", "\n", RegexOptions.IgnoreCase);
        // Remove all remaining HTML tags
        text = Regex.Replace(text, @"<[^>]+>", string.Empty);
        // Decode common HTML entities
        text = text
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&nbsp;", " ");
        // Collapse multiple blank lines
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
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
            ? $@"<table role=""presentation"" cellpadding=""0"" cellspacing=""0"" style=""margin-top:28px"">
                   <tr><td style=""border-radius:6px;background:#e53935"">
                     <a href=""{ctaUrl}"" class=""btn"">{ctaLabel ?? "Przejdź"}</a>
                   </td></tr>
                 </table>"
            : string.Empty;

        return $@"<!DOCTYPE html>
<html lang=""pl"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<meta name=""color-scheme"" content=""dark"">
<meta name=""supported-color-schemes"" content=""dark"">
<title>{title}</title>
<style>
  *, *::before, *::after {{ box-sizing: border-box; margin: 0; padding: 0; }}
  body {{
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Arial, sans-serif;
    background-color: #080808;
    color: #c8c8c8;
    padding: 32px 16px;
    -webkit-font-smoothing: antialiased;
  }}
  .wrap {{
    max-width: 560px;
    margin: 0 auto;
    background: #101010;
    border: 1px solid #1f1f1f;
    border-radius: 12px;
    overflow: hidden;
  }}
  .accent-bar {{
    height: 3px;
    background: linear-gradient(90deg, #e53935 0%, #ff6659 100%);
  }}
  .hdr {{
    background: #0c0c0c;
    padding: 22px 32px;
    border-bottom: 1px solid #1a1a1a;
    display: flex;
    align-items: center;
    gap: 10px;
  }}
  .logo-text {{
    font-size: 18px;
    font-weight: 900;
    color: #ffffff;
    letter-spacing: 1px;
    text-transform: uppercase;
  }}
  .logo-text span {{ color: #e53935; }}
  .logo-dot {{
    width: 6px;
    height: 6px;
    background: #e53935;
    border-radius: 50%;
    display: inline-block;
    margin-left: 2px;
    vertical-align: middle;
  }}
  .body {{ padding: 36px 32px 32px; }}
  .eyebrow {{
    font-size: 11px;
    font-weight: 600;
    letter-spacing: 1.5px;
    text-transform: uppercase;
    color: #e53935;
    margin-bottom: 12px;
  }}
  .title {{
    font-size: 22px;
    font-weight: 700;
    color: #ffffff;
    line-height: 1.3;
    margin-bottom: 16px;
    letter-spacing: -0.3px;
  }}
  .text {{
    font-size: 14px;
    color: #888;
    line-height: 1.75;
  }}
  .text strong {{ color: #ccc; }}
  .highlight {{
    background: #141414;
    border: 1px solid #222;
    border-left: 3px solid #e53935;
    border-radius: 6px;
    padding: 14px 16px;
    margin: 20px 0 0;
  }}
  .highlight p {{ font-size: 12px; color: #555; line-height: 1.6; margin: 0; }}
  .btn {{
    display: inline-block;
    background: #e53935;
    color: #ffffff !important;
    padding: 13px 28px;
    border-radius: 6px;
    text-decoration: none;
    font-weight: 700;
    font-size: 14px;
    letter-spacing: 0.3px;
  }}
  .divider {{
    height: 1px;
    background: #1a1a1a;
    margin: 28px 0 0;
  }}
  .ftr {{
    background: #0c0c0c;
    border-top: 1px solid #1a1a1a;
    padding: 18px 32px;
    font-size: 11px;
    color: #3a3a3a;
    line-height: 1.7;
  }}
  .ftr a {{ color: #555; text-decoration: none; }}
  .ftr a:hover {{ color: #e53935; }}
  @media (max-width: 480px) {{
    .body {{ padding: 24px 20px 20px; }}
    .hdr {{ padding: 18px 20px; }}
    .ftr {{ padding: 14px 20px; }}
    .title {{ font-size: 19px; }}
  }}
</style>
</head>
<body>
  <div class=""wrap"">
    <div class=""accent-bar""></div>
    <div class=""hdr"">
      <span class=""logo-text"">CARI<span>ZO</span></span>
    </div>
    <div class=""body"">
      <p class=""eyebrow"">Wiadomość od CARIZO</p>
      <p class=""title"">{title}</p>
      <p class=""text"">{mainText}</p>
      {details}
      {cta}
    </div>
    <div class=""ftr"">
      Ta wiadomość została wysłana automatycznie · <a href=""https://carizo.pl"">carizo.pl</a><br>
      Zarządzaj powiadomieniami w <a href=""https://carizo.pl/dashboard/ustawienia"">ustawieniach konta</a>.
    </div>
  </div>
</body>
</html>";
    }
}
