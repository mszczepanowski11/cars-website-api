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
        int year = DateTime.UtcNow.Year;

        var detailsBlock = detailsHtml != null ? $@"
              <!-- Details box -->
              <tr><td style=""padding:20px 0 0 0"">
                <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"">
                  <tr>
                    <td width=""3"" style=""background:#8B0D1D;border-radius:3px 0 0 3px""></td>
                    <td style=""background:#0a0a0a;border:1px solid #1a1a1a;border-left:none;border-radius:0 8px 8px 0;padding:14px 18px"">
                      <div style=""font-size:13px;color:#555555;line-height:1.65;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Arial,sans-serif"">{detailsHtml}</div>
                    </td>
                  </tr>
                </table>
              </td></tr>" : string.Empty;

        var ctaBlock = ctaUrl != null ? $@"
              <!-- CTA button -->
              <tr><td style=""padding:30px 0 0 0"">
                <!--[if mso]><v:roundrect xmlns:v=""urn:schemas-microsoft-com:vml"" xmlns:w=""urn:schemas-microsoft-com:office:word"" href=""{ctaUrl}"" style=""height:46px;v-text-anchor:middle;width:200px;"" arcsize=""22%"" strokecolor=""#8B0D1D"" fillcolor=""#8B0D1D""><w:anchorlock/><center style=""color:#ffffff;font-family:Arial,sans-serif;font-size:14px;font-weight:700"">{ctaLabel ?? "Przejdź"}</center></v:roundrect><![endif]-->
                <!--[if !mso]><!-->
                <a href=""{ctaUrl}"" style=""display:inline-block;background:#8B0D1D;color:#ffffff;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Arial,sans-serif;font-size:14px;font-weight:700;text-decoration:none;padding:14px 32px;border-radius:10px;letter-spacing:0.2px;line-height:1"">{ctaLabel ?? "Przejdź"}</a>
                <!--<![endif]-->
              </td></tr>" : string.Empty;

        return $@"<!DOCTYPE html>
<html lang=""pl"" xmlns:v=""urn:schemas-microsoft-com:vml"" xmlns:o=""urn:schemas-microsoft-com:office:office"">
<head>
  <meta charset=""UTF-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1.0"">
  <meta name=""x-apple-disable-message-reformatting"">
  <meta name=""color-scheme"" content=""dark"">
  <meta name=""supported-color-schemes"" content=""dark"">
  <!--[if mso]><noscript><xml><o:OfficeDocumentSettings><o:PixelsPerInch>96</o:PixelsPerInch></o:OfficeDocumentSettings></xml></noscript><![endif]-->
  <title>{title}</title>
  <style>
    body,table,td,p,a,li{{-webkit-text-size-adjust:100%;-ms-text-size-adjust:100%;margin:0;padding:0}}
    table{{border-spacing:0;border-collapse:collapse;mso-table-lspace:0pt;mso-table-rspace:0pt}}
    img{{border:0;height:auto;line-height:100%;outline:none;text-decoration:none;-ms-interpolation-mode:bicubic;display:block}}
    body{{background-color:#050505;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Arial,sans-serif}}
    a.btn:hover{{opacity:0.88 !important}}
    .ftr-link:hover{{color:#aaaaaa !important}}
    @media only screen and (max-width:620px){{
      .outer-pad{{padding:16px 0 !important}}
      .card{{border-radius:12px !important}}
      .hdr-cell{{padding:20px 24px !important}}
      .body-cell{{padding:28px 24px 24px !important}}
      .ftr-cell{{padding:22px 24px !important}}
      .title-text{{font-size:20px !important}}
      .btn-block{{display:block !important;text-align:center !important}}
    }}
  </style>
</head>
<body style=""margin:0;padding:0;background-color:#050505"">

<!-- Outer wrapper -->
<table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" bgcolor=""#050505"">
  <tr><td class=""outer-pad"" style=""padding:40px 16px"" align=""center"">

    <!-- Email card -->
    <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""600"" style=""max-width:600px;width:100%"">

      <!-- Top accent bar -->
      <!--[if mso]><tr><td bgcolor=""#8B0D1D"" height=""3"" style=""font-size:3px;line-height:3px"">&nbsp;</td></tr><![endif]-->
      <!--[if !mso]><!-->
      <tr><td style=""background:linear-gradient(90deg,#8B0D1D 0%,#b01424 100%);height:3px;font-size:3px;line-height:3px"">&nbsp;</td></tr>
      <!--<![endif]-->

      <!-- Header -->
      <tr><td class=""hdr-cell"" bgcolor=""#080808"" style=""background-color:#080808;padding:26px 40px;border-left:1px solid #1a1a1a;border-right:1px solid #1a1a1a;border-bottom:1px solid #141414"" align=""left"">
        <!--[if mso]>
        <span style=""font-family:'Helvetica Neue',Helvetica,Arial,sans-serif;font-size:22px;font-weight:200;color:#ffffff;letter-spacing:4px"">CARIZO</span>
        <![endif]-->
        <!--[if !mso]><!-->
        <span style=""font-family:'Helvetica Neue',Helvetica,Arial,sans-serif;font-size:22px;font-weight:200;color:#ffffff;letter-spacing:4px"">CARI<span style=""color:#8B0D1D"">Z</span>O</span>
        <!--<![endif]-->
      </td></tr>

      <!-- Body -->
      <tr><td class=""body-cell"" bgcolor=""#0d0d0d"" style=""background-color:#0d0d0d;padding:40px 40px 36px;border-left:1px solid #1a1a1a;border-right:1px solid #1a1a1a"">
        <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"">

          <!-- Label -->
          <tr><td style=""padding:0 0 14px 0"">
            <span style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Arial,sans-serif;font-size:11px;font-weight:600;letter-spacing:1.8px;text-transform:uppercase;color:#8B0D1D"">Wiadomość od CARIZO</span>
          </td></tr>

          <!-- Title -->
          <tr><td style=""padding:0 0 18px 0"">
            <h1 class=""title-text"" style=""margin:0;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Arial,sans-serif;font-size:24px;font-weight:700;color:#ffffff;line-height:1.3;letter-spacing:-0.3px"">{title}</h1>
          </td></tr>

          <!-- Main text -->
          <tr><td>
            <p style=""margin:0;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Arial,sans-serif;font-size:15px;color:#888888;line-height:1.75"">{mainText}</p>
          </td></tr>
          {detailsBlock}
          {ctaBlock}

        </table>
      </td></tr>

      <!-- Divider -->
      <tr><td bgcolor=""#0d0d0d"" style=""background-color:#0d0d0d;border-left:1px solid #1a1a1a;border-right:1px solid #1a1a1a"">
        <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""><tr><td style=""height:1px;background:#141414;font-size:1px;line-height:1px"">&nbsp;</td></tr></table>
      </td></tr>

      <!-- Footer -->
      <tr><td class=""ftr-cell"" bgcolor=""#080808"" style=""background-color:#080808;padding:26px 40px 28px;border:1px solid #1a1a1a;border-top:none;border-radius:0 0 20px 20px"">
        <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"">

          <!-- Footer logo -->
          <tr><td style=""padding:0 0 14px 0"">
            <!--[if mso]>
            <span style=""font-family:'Helvetica Neue',Helvetica,Arial,sans-serif;font-size:16px;font-weight:200;color:#333333;letter-spacing:3px"">CARIZO</span>
            <![endif]-->
            <!--[if !mso]><!-->
            <span style=""font-family:'Helvetica Neue',Helvetica,Arial,sans-serif;font-size:16px;font-weight:200;color:#444444;letter-spacing:3px"">CARI<span style=""color:#6b0f1a"">Z</span>O</span>
            <!--<![endif]-->
          </td></tr>

          <!-- Footer links -->
          <tr><td style=""padding:0 0 14px 0"">
            <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"">
              <tr>
                <td style=""padding-right:16px""><a href=""https://carizo.eu"" class=""ftr-link"" style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Arial,sans-serif;font-size:12px;color:#444444;text-decoration:none"">carizo.eu</a></td>
                <td style=""padding-right:16px""><a href=""https://carizo.eu/dashboard/ustawienia"" class=""ftr-link"" style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Arial,sans-serif;font-size:12px;color:#444444;text-decoration:none"">Ustawienia powiadomień</a></td>
                <td style=""padding-right:16px""><a href=""https://carizo.eu/dashboard"" class=""ftr-link"" style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Arial,sans-serif;font-size:12px;color:#444444;text-decoration:none"">Panel konta</a></td>
                <td><a href=""https://carizo.eu/#contact"" class=""ftr-link"" style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Arial,sans-serif;font-size:12px;color:#444444;text-decoration:none"">Kontakt</a></td>
              </tr>
            </table>
          </td></tr>

          <!-- Copyright + auto-message note -->
          <tr><td>
            <p style=""margin:0;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Arial,sans-serif;font-size:11px;color:#2e2e2e;line-height:1.7"">
              Ta wiadomość została wysłana automatycznie — prosimy na nią nie odpowiadać.<br>
              © {year} CARIZO. Wszelkie prawa zastrzeżone. · <a href=""https://carizo.eu/polityka-prywatnosci"" style=""color:#333333;text-decoration:none"">Polityka prywatności</a>
            </p>
          </td></tr>

        </table>
      </td></tr>

    </table>
    <!-- / Email card -->

  </td></tr>
</table>
<!-- / Outer wrapper -->

</body>
</html>";
    }
}
