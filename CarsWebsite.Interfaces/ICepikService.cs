namespace cars_website_api.CarsWebsite.Interfaces;

public class CepikVehicleData
{
    public string? Vin { get; set; }
    public string? Marka { get; set; }
    public string? Model { get; set; }
    public int? RokProdukcji { get; set; }
    public string? RodzajPaliwa { get; set; }
    public int? PojemnoscSkokowa { get; set; }
    public int? MocSilnika { get; set; }
    public string? Kolor { get; set; }
    public string? RodzajPojazdu { get; set; }
    public int? LiczbaMiejsc { get; set; }
    public int? LiczbaDrzwi { get; set; }
    public string? DataPierwszejRejestracji { get; set; }
}

public class CepikLookupResult
{
    public bool Success { get; set; }
    // "not_configured" | "not_found" | "multiple_matches" | "api_error" | "unrecognized_response"
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public CepikVehicleData? Vehicle { get; set; }

    public static CepikLookupResult Ok(CepikVehicleData vehicle) => new() { Success = true, Vehicle = vehicle };
    public static CepikLookupResult Fail(string code, string message) => new() { Success = false, ErrorCode = code, ErrorMessage = message };
}

public interface ICepikService
{
    // wojewodztwoCode: 2-digit TERYT code (e.g. "14" = mazowieckie). CEPiK's /pojazdy endpoint
    // requires this plus a date range as its base filter - VIN is applied as an additional
    // `filter[vin]` on top, best-effort (see CepikService for caveats - this integration is
    // unverified against the real API pending an access token, per crispy-riding-mochi.md follow-up).
    Task<CepikLookupResult> LookupByVinAsync(string wojewodztwoCode, string vin);
}
