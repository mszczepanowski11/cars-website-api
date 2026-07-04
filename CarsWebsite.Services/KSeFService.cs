using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;

namespace cars_website_api.CarsWebsite.Services;

public class KSeFService : IKSeFService
{
    private static readonly XNamespace Ns = "http://crd.gov.pl/wzor/2023/06/29/12648/";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<KSeFService> _logger;

    public KSeFService(HttpClient http, IConfiguration config, ILogger<KSeFService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<string?> SendInvoiceAsync(Invoice invoice, List<Payment> payments)
    {
        var token = Environment.GetEnvironmentVariable("KSEF_TOKEN") ?? _config["KSeF:Token"];
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogInformation("[KSeF] Token not configured, skipping invoice {Nr}", invoice.InvoiceNumber);
            return null;
        }

        var apiUrl = (Environment.GetEnvironmentVariable("KSEF_API_URL") ?? _config["KSeF:ApiUrl"] ?? "https://api.ksef.mf.gov.pl").TrimEnd('/');
        var sellerNip = (_config["Invoice:SellerNip"] ?? "9452331007").Trim();

        var firstPayment = payments.FirstOrDefault();
        var buyerNip = (firstPayment?.BillingNip ?? invoice.User?.Nip)?.Replace("-", "").Replace(" ", "").Trim();
        if (string.IsNullOrWhiteSpace(buyerNip))
        {
            _logger.LogInformation("[KSeF] Buyer NIP missing for {Nr}, skipping KSeF", invoice.InvoiceNumber);
            return null;
        }

        try
        {
            var sessionToken = await GetSessionTokenAsync(apiUrl, sellerNip, token);
            var xmlBytes = BuildFaVatXmlBytes(invoice, payments, sellerNip, buyerNip, firstPayment);
            var elementRef = await SendToKSeFAsync(apiUrl, sessionToken, xmlBytes);
            var ksefNumber = await PollStatusAsync(apiUrl, sessionToken, elementRef);
            await TerminateSessionAsync(apiUrl, sessionToken);
            _logger.LogInformation("[KSeF] Invoice {Nr} sent to KSeF → {KSeF}", invoice.InvoiceNumber, ksefNumber);
            return ksefNumber;
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException != null ? $" | inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}" : "";
            _logger.LogError(ex, "[KSeF] Send failed for invoice {Nr} -- {ExType}: {ExMessage}{Inner}",
                invoice.InvoiceNumber, ex.GetType().Name, ex.Message, inner);
            return null;
        }
    }

    // ── Session auth ──────────────────────────────────────────────────────────

    private async Task<string> GetSessionTokenAsync(string apiUrl, string nip, string apiToken)
    {
        var challengeResp = await _http.GetFromJsonAsync<KSeFChallenge>(
            $"{apiUrl}/api/online/Session/AuthorisationChallenge?identifier={nip}&identifierType=onip",
            JsonOpts) ?? throw new InvalidOperationException("Null KSeF challenge response");

        var encrypted = EncryptChallenge(challengeResp.Challenge, apiToken);

        var initBody = new { contextIdentifier = new { type = "onip", identifier = nip }, token = new { value = encrypted } };
        var r = await _http.PostAsJsonAsync($"{apiUrl}/api/online/Session/InitialisationToken", initBody, JsonOpts);
        if (!r.IsSuccessStatusCode)
        {
            var body = await r.Content.ReadAsStringAsync();
            throw new HttpRequestException($"KSeF InitialisationToken failed {r.StatusCode}: {body}");
        }

        var init = await r.Content.ReadFromJsonAsync<KSeFInitResponse>(JsonOpts)
            ?? throw new InvalidOperationException("Null KSeF init response");
        return init.SessionToken.Token;
    }

    // Challenge bytes XOR SHA-256(token) — standard KSeF token auth
    private static string EncryptChallenge(string challenge, string apiToken)
    {
        var challengeBytes = Encoding.UTF8.GetBytes(challenge);
        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(apiToken));
        var enc = new byte[challengeBytes.Length];
        for (int i = 0; i < challengeBytes.Length; i++)
            enc[i] = (byte)(challengeBytes[i] ^ keyBytes[i % 32]);
        return Convert.ToBase64String(enc);
    }

    // ── Invoice send ──────────────────────────────────────────────────────────

    private async Task<string> SendToKSeFAsync(string apiUrl, string sessionToken, byte[] xmlBytes)
    {
        var body = new
        {
            invoiceHash = new
            {
                fileSize = xmlBytes.Length,
                hashSHA = new { algorithm = "SHA-256", encoding = "Base64", value = Convert.ToBase64String(SHA256.HashData(xmlBytes)) }
            },
            invoicePayload = new { type = "plain", invoiceBody = Convert.ToBase64String(xmlBytes) }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{apiUrl}/api/online/Invoice/Send");
        req.Headers.Add("SessionToken", sessionToken);
        req.Content = JsonContent.Create(body, options: JsonOpts);

        var r = await _http.SendAsync(req);
        if (!r.IsSuccessStatusCode)
        {
            var errBody = await r.Content.ReadAsStringAsync();
            throw new HttpRequestException($"KSeF Invoice/Send failed {r.StatusCode}: {errBody}");
        }

        var resp = await r.Content.ReadFromJsonAsync<KSeFSendResponse>(JsonOpts)
            ?? throw new InvalidOperationException("Null KSeF send response");
        return resp.ReferenceNumber;
    }

    private async Task<string?> PollStatusAsync(string apiUrl, string sessionToken, string elementRef)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(3000);
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{apiUrl}/api/online/Invoice/Status/{elementRef}");
            req.Headers.Add("SessionToken", sessionToken);
            var r = await _http.SendAsync(req);
            if (!r.IsSuccessStatusCode) continue;

            var status = await r.Content.ReadFromJsonAsync<KSeFStatusResponse>(JsonOpts);
            var ksefNr = status?.InvoiceStatus?.KsefReferenceNumber;
            if (!string.IsNullOrEmpty(ksefNr)) return ksefNr;
        }
        _logger.LogWarning("[KSeF] Status poll timed out for element {Ref}", elementRef);
        return null;
    }

    private async Task TerminateSessionAsync(string apiUrl, string sessionToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{apiUrl}/api/online/Session/Terminate");
        req.Headers.Add("SessionToken", sessionToken);
        await _http.SendAsync(req);
    }

    // ── FA_VAT XML ────────────────────────────────────────────────────────────

    private byte[] BuildFaVatXmlBytes(Invoice invoice, List<Payment> payments, string sellerNip, string buyerNip, Payment? firstPayment)
    {
        var ci = CultureInfo.InvariantCulture;
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff+00:00");

        int issueYear = invoice.Month == 12 ? invoice.Year + 1 : invoice.Year;
        int issueMonth = invoice.Month == 12 ? 1 : invoice.Month + 1;
        var issueDateStr = new DateTime(issueYear, issueMonth, 1).ToString("yyyy-MM-dd");
        var issueDateM = new DateTime(issueYear, issueMonth, 1).ToString("yyyy-MM");
        var periodStart = new DateTime(invoice.Year, invoice.Month, 1).ToString("yyyy-MM-dd");
        var periodEnd = new DateTime(invoice.Year, invoice.Month, DateTime.DaysInMonth(invoice.Year, invoice.Month)).ToString("yyyy-MM-dd");

        var sellerName = _config["Invoice:SellerName"] ?? "CARIZO Wiktor Niezgoda";
        var buyerName = firstPayment?.BillingName
            ?? (invoice.User?.AccountType == AccountType.Business && !string.IsNullOrWhiteSpace(invoice.User.CompanyName)
                ? invoice.User.CompanyName
                : $"{invoice.User?.Name} {invoice.User?.Surname}".Trim());

        var lineItems = payments.Select((p, idx) =>
        {
            var net = Math.Round(p.Amount / 1.23m, 2);
            return (Line: idx + 1, Desc: p.ServiceDescription, Net: net);
        }).ToList();

        var podmiot2 = BuildPodmiot2(buyerNip, buyerName, firstPayment);

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ns + "Faktura",
                new XElement(Ns + "Naglowek",
                    new XElement(Ns + "KodFormularza",
                        new XAttribute("kodSystemowy", "FA (2)"),
                        new XAttribute("wersjaSchemy", "1-0E"),
                        "FA"),
                    new XElement(Ns + "WariantFormularza", "2"),
                    new XElement(Ns + "DataWytworzeniaFa", now),
                    new XElement(Ns + "SystemInfo", "CARIZO")),
                new XElement(Ns + "Podmiot1",
                    new XElement(Ns + "DaneIdentyfikacyjne",
                        new XElement(Ns + "NIP", sellerNip),
                        new XElement(Ns + "Nazwa", sellerName)),
                    new XElement(Ns + "Adres",
                        new XElement(Ns + "KodKraju", "PL"),
                        new XElement(Ns + "Ulica", "ul. Henryka Pachońskiego"),
                        new XElement(Ns + "NrDomu", "7"),
                        new XElement(Ns + "NrLokalu", "60"),
                        new XElement(Ns + "Miejscowosc", "Kraków"),
                        new XElement(Ns + "KodPocztowy", "31-223")),
                    new XElement(Ns + "DaneKontaktowe",
                        new XElement(Ns + "Email", "kontakt@carizo.eu"))),
                podmiot2,
                new XElement(Ns + "Fa",
                    new XElement(Ns + "KodWaluty", "PLN"),
                    new XElement(Ns + "P_1", issueDateStr),
                    new XElement(Ns + "P_1M", issueDateM),
                    new XElement(Ns + "P_2", invoice.InvoiceNumber),
                    new XElement(Ns + "OkresFa",
                        new XElement(Ns + "P_6_Od", periodStart),
                        new XElement(Ns + "P_6_Do", periodEnd)),
                    new XElement(Ns + "P_13_1", invoice.NetAmount.ToString("F2", ci)),
                    new XElement(Ns + "P_14_1", invoice.VatAmount.ToString("F2", ci)),
                    new XElement(Ns + "P_15", invoice.TotalAmount.ToString("F2", ci)),
                    new XElement(Ns + "Adnotacje",
                        new XElement(Ns + "P_16", "2"),
                        new XElement(Ns + "P_17", "2"),
                        new XElement(Ns + "P_18", "2"),
                        new XElement(Ns + "P_18A", "2"),
                        new XElement(Ns + "P_19", "2"),
                        new XElement(Ns + "P_22", "2"),
                        new XElement(Ns + "P_23", "2"),
                        new XElement(Ns + "P_PMarzy", "2")),
                    new XElement(Ns + "RodzajFaktury", "VAT"),
                    lineItems.Select(li =>
                        new XElement(Ns + "FaWiersz",
                            new XElement(Ns + "NrWierszaFa", li.Line.ToString()),
                            new XElement(Ns + "P_7", li.Desc),
                            new XElement(Ns + "P_8A", "szt."),
                            new XElement(Ns + "P_8B", "1"),
                            new XElement(Ns + "P_9A", li.Net.ToString("F2", ci)),
                            new XElement(Ns + "P_11", li.Net.ToString("F2", ci)),
                            new XElement(Ns + "P_12", "23"))))));

        var sb = new StringBuilder();
        using var writer = new System.IO.StringWriter(sb);
        doc.Save(writer);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private XElement BuildPodmiot2(string buyerNip, string buyerName, Payment? firstPayment)
    {
        var elems = new List<object>
        {
            new XElement(Ns + "DaneIdentyfikacyjne",
                new XElement(Ns + "NIP", buyerNip),
                new XElement(Ns + "Nazwa", buyerName))
        };

        var street = firstPayment?.BillingStreet ?? "";
        var city = firstPayment?.BillingCity ?? "";
        var postal = firstPayment?.BillingPostalCode ?? "";

        if (!string.IsNullOrWhiteSpace(city))
        {
            var (ulica, nrDomu, nrLokalu) = ParseStreet(street);
            var adresElems = new List<XElement>
            {
                new(Ns + "KodKraju", "PL"),
                new(Ns + "Ulica", ulica),
                new(Ns + "NrDomu", nrDomu),
            };
            if (!string.IsNullOrEmpty(nrLokalu)) adresElems.Add(new XElement(Ns + "NrLokalu", nrLokalu));
            adresElems.Add(new XElement(Ns + "Miejscowosc", city));
            if (!string.IsNullOrEmpty(postal)) adresElems.Add(new XElement(Ns + "KodPocztowy", postal));

            elems.Add(new XElement(Ns + "Adres", adresElems));
        }

        return new XElement(Ns + "Podmiot2", elems);
    }

    private static (string ulica, string nrDomu, string? nrLokalu) ParseStreet(string street)
    {
        if (string.IsNullOrWhiteSpace(street)) return ("b/n", "1", null);
        var m = Regex.Match(street.Trim(), @"^(.*?)\s+(\d+[a-zA-Z]?)(?:[/\-](\w+))?$");
        if (m.Success)
            return (m.Groups[1].Value.Trim(), m.Groups[2].Value, m.Groups[3].Success ? m.Groups[3].Value : null);
        return (street.Trim(), "1", null);
    }

    // ── Response DTOs ─────────────────────────────────────────────────────────

    private record KSeFChallenge(
        [property: JsonPropertyName("timestamp")] string Timestamp,
        [property: JsonPropertyName("challenge")] string Challenge);

    private record KSeFInitResponse(
        [property: JsonPropertyName("referenceNumber")] string ReferenceNumber,
        [property: JsonPropertyName("sessionToken")] KSeFSessionTokenWrapper SessionToken);

    private record KSeFSessionTokenWrapper(
        [property: JsonPropertyName("token")] string Token);

    private record KSeFSendResponse(
        [property: JsonPropertyName("referenceNumber")] string ReferenceNumber);

    private record KSeFStatusResponse(
        [property: JsonPropertyName("referenceNumber")] string ReferenceNumber,
        [property: JsonPropertyName("invoiceStatus")] KSeFInvoiceStatus? InvoiceStatus);

    private record KSeFInvoiceStatus(
        [property: JsonPropertyName("ksefReferenceNumber")] string? KsefReferenceNumber,
        [property: JsonPropertyName("invoiceStatusCode")] int InvoiceStatusCode);
}
