using System.Net.Http.Headers;
using System.Text.Json;
using cars_website_api.CarsWebsite.Interfaces;

namespace cars_website_api.CarsWebsite.Services;

// Integration with CEPiK (Centralna Ewidencja Pojazdów i Kierowców), Poland's central vehicle
// registry - https://api.cepik.gov.pl/doc. Lets a seller pre-fill the add-advert form from their
// VIN instead of typing everything by hand.
//
// IMPORTANT / UNVERIFIED: as of writing, this repo has no CEPiK access token yet (one must be
// requested via the developer portal at cpa.gov.pl), and the live API documentation at
// api.cepik.gov.pl/doc returned 403 to every automated fetch attempt during development - it could
// not be read directly. The request/response shape and auth header below are built from CEPiK's
// publicly documented query parameters (dane.gov.pl dataset description) and from how comparable
// Polish gov.pl JSON:API-style registries behave, NOT from a verified live response. Once a real
// token is configured (CEPIK_API_TOKEN), the first live call MUST be checked against these
// assumptions and this file adjusted if wrong:
//   1. Auth: currently sent as `Authorization: Bearer {token}`. If CEPiK instead expects the token
//      as a query string parameter or a different header name, update BuildRequest below.
//   2. Response envelope: currently parsed as JSON:API (`{ "data": [ { "attributes": {...} } ] }`),
//      falling back to a flat array of objects. If neither matches, ParseVehicle logs the raw body
//      and returns "unrecognized_response" instead of guessing further.
//   3. Field names: assumed kebab-case (marka, model, rok-produkcji, rodzaj-paliwa,
//      pojemnosc-skokowa-silnika, moc-silnika, kolor, rodzaj-pojazdu, liczba-miejsc, liczba-drzwi,
//      data-pierwszej-rejestracji-pojazdu, vin) per CEPiK's documented filter[...] parameter names -
//      confirm these match the actual attributes keys once real data comes back.
//
// CEPiK's /pojazdy endpoint is fundamentally a bulk/date-range query (requires wojewodztwo +
// data-od as its base filter, designed for statistical exports), not a single-VIN lookup - VIN is
// applied here as an additional filter[vin] on top, best-effort. It may return zero, one, or
// (unexpectedly) more than one match; all three are handled explicitly below.
public class CepikService : ICepikService
{
    private const string DefaultApiUrl = "https://api.cepik.gov.pl";
    // Earliest possible first-registration date CEPiK could plausibly hold - data-od is mandatory
    // on every /pojazdy call, and we don't know the vehicle's registration date up front (that's
    // exactly what we're trying to find out), so this covers the entire practical range.
    private const string EarliestDataOd = "19900101";

    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<CepikService> _logger;

    public CepikService(HttpClient http, IConfiguration config, ILogger<CepikService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<CepikLookupResult> LookupByVinAsync(string wojewodztwoCode, string vin)
    {
        var token = Environment.GetEnvironmentVariable("CEPIK_API_TOKEN") ?? _config["Cepik:Token"];
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogInformation("[CEPiK] Token not configured, skipping lookup for VIN {Vin}", vin);
            return CepikLookupResult.Fail("not_configured", "Integracja z CEPiK nie jest jeszcze skonfigurowana.");
        }

        var apiUrl = (Environment.GetEnvironmentVariable("CEPIK_API_URL") ?? _config["Cepik:ApiUrl"] ?? DefaultApiUrl).TrimEnd('/');
        var url = $"{apiUrl}/pojazdy?wojewodztwo={Uri.EscapeDataString(wojewodztwoCode)}&data-od={EarliestDataOd}&filter[vin]={Uri.EscapeDataString(vin)}";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var resp = await _http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[CEPiK] Lookup failed {Status} for VIN {Vin}: {Body}", resp.StatusCode, vin, body);
                return CepikLookupResult.Fail("api_error", $"CEPiK zwrócił błąd ({(int)resp.StatusCode}).");
            }

            return ParseVehicles(body, vin);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CEPiK] Lookup threw for VIN {Vin}", vin);
            return CepikLookupResult.Fail("api_error", "Nie udało się połączyć z CEPiK.");
        }
    }

    private CepikLookupResult ParseVehicles(string body, string vin)
    {
        List<JsonElement> records;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement.Clone();

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array)
            {
                // JSON:API-style envelope: data[].attributes
                records = dataEl.EnumerateArray()
                    .Select(el => el.TryGetProperty("attributes", out var attrs) ? attrs.Clone() : el.Clone())
                    .ToList();
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                records = root.EnumerateArray().Select(el => el.Clone()).ToList();
            }
            else
            {
                _logger.LogWarning("[CEPiK] Unrecognized response shape for VIN {Vin}: {Body}", vin, body);
                return CepikLookupResult.Fail("unrecognized_response", "Nieznany format odpowiedzi CEPiK.");
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[CEPiK] Failed to parse response for VIN {Vin}: {Body}", vin, body);
            return CepikLookupResult.Fail("unrecognized_response", "Nie udało się odczytać odpowiedzi CEPiK.");
        }

        if (records.Count == 0)
            return CepikLookupResult.Fail("not_found", "Nie znaleziono pojazdu o podanym numerze VIN w wybranym województwie.");

        if (records.Count > 1)
            return CepikLookupResult.Fail("multiple_matches", $"Znaleziono {records.Count} pasujących pojazdów - CEPiK nie zwrócił jednoznacznego wyniku dla tego VIN.");

        return CepikLookupResult.Ok(MapVehicle(records[0]));
    }

    private static CepikVehicleData MapVehicle(JsonElement el)
    {
        string? Str(string key) => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        int? Int(string key)
        {
            if (!el.TryGetProperty(key, out var v)) return null;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
            if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var n2)) return n2;
            return null;
        }

        return new CepikVehicleData
        {
            Vin = Str("vin"),
            Marka = Str("marka"),
            Model = Str("model"),
            RokProdukcji = Int("rok-produkcji"),
            RodzajPaliwa = Str("rodzaj-paliwa"),
            PojemnoscSkokowa = Int("pojemnosc-skokowa-silnika"),
            MocSilnika = Int("moc-silnika"),
            Kolor = Str("kolor"),
            RodzajPojazdu = Str("rodzaj-pojazdu"),
            LiczbaMiejsc = Int("liczba-miejsc"),
            LiczbaDrzwi = Int("liczba-drzwi"),
            DataPierwszejRejestracji = Str("data-pierwszej-rejestracji-pojazdu"),
        };
    }
}
