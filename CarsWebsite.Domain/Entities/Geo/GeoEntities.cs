using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.Domain.Entities;

// Global reference-data core (Faza 0 of the global rearchitecture). These tables replace the
// Poland-hardcoded location/currency/language assumptions with proper keyed reference data that
// every other entity (Advert, DirectoryCompany, User, VehicleCategory) can point at. Everything
// here is seedable from ISO 3166 / 4217 / 639, IANA tzdata and GeoNames - no strings in code.

public class Continent
{
    public int Id { get; set; }
    [MaxLength(2)] public string Code { get; set; } = string.Empty;   // ISO continent, e.g. "EU","AS"
    [MaxLength(80)] public string Name { get; set; } = string.Empty;  // English base name; localized via Localizations later
}

public class Currency
{
    public int Id { get; set; }
    [MaxLength(3)]  public string Iso { get; set; } = string.Empty;   // ISO 4217, e.g. "EUR"
    [MaxLength(8)]  public string Symbol { get; set; } = string.Empty;// "€","$","zł"
    [MaxLength(60)] public string Name { get; set; } = string.Empty;
    public byte Decimals { get; set; } = 2;                           // JPY=0, EUR=2, BHD=3
    [MaxLength(4)] public string SymbolPosition { get; set; } = "pre";// "pre" | "post"
    public bool IsActive { get; set; } = true;
}

public class Language
{
    public int Id { get; set; }
    [MaxLength(2)]  public string Iso1 { get; set; } = string.Empty;  // ISO 639-1, e.g. "de"
    [MaxLength(60)] public string Endonym { get; set; } = string.Empty; // "Deutsch"
    [MaxLength(60)] public string EnglishName { get; set; } = string.Empty;
    public bool IsRtl { get; set; }
    public bool IsActive { get; set; } = true;
}

public class AppTimeZone
{
    public int Id { get; set; }
    [MaxLength(60)] public string IanaName { get; set; } = string.Empty; // "Europe/Warsaw"
    public int UtcOffsetMinutes { get; set; }                            // standard offset, informational
    [MaxLength(80)] public string DisplayName { get; set; } = string.Empty;
}

public class Country
{
    public int Id { get; set; }
    [MaxLength(2)]  public string Iso2 { get; set; } = string.Empty;  // "PL"
    [MaxLength(3)]  public string Iso3 { get; set; } = string.Empty;  // "POL"
    [MaxLength(80)] public string Name { get; set; } = string.Empty;  // English base name
    [MaxLength(80)] public string NativeName { get; set; } = string.Empty; // endonym "Polska"

    public int? ContinentId { get; set; }
    public Continent? Continent { get; set; }

    public int? DefaultCurrencyId { get; set; }
    public Currency? DefaultCurrency { get; set; }

    public int? DefaultLanguageId { get; set; }
    public Language? DefaultLanguage { get; set; }

    public int? DefaultTimeZoneId { get; set; }
    public AppTimeZone? DefaultTimeZone { get; set; }

    [MaxLength(8)]  public string? PhonePrefix { get; set; }          // "+48"
    [MaxLength(8)]  public string MeasurementSystem { get; set; } = "metric"; // "metric" | "imperial"
    [MaxLength(1)]  public string DrivingSide { get; set; } = "R";    // "L" | "R"
    [MaxLength(24)] public string? PostalCodeRegex { get; set; }
    public bool IsActive { get; set; } = true;                        // sanctions / not-yet-launched

    public ICollection<Region> Regions { get; set; } = new List<Region>();
}

public class Region
{
    public int Id { get; set; }
    public int CountryId { get; set; }
    public Country? Country { get; set; }
    [MaxLength(10)] public string? Code { get; set; }                 // ISO 3166-2 suffix, e.g. "MZ"
    [MaxLength(120)] public string Name { get; set; } = string.Empty;
    [MaxLength(30)] public string Type { get; set; } = "region";      // "state","voivodeship","province"...
}

public class City
{
    public long Id { get; set; }
    public int CountryId { get; set; }
    public Country? Country { get; set; }
    public int? RegionId { get; set; }
    public Region? Region { get; set; }
    [MaxLength(160)] public string Name { get; set; } = string.Empty;      // endonym
    [MaxLength(160)] public string AsciiName { get; set; } = string.Empty; // for accent-insensitive search
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int Population { get; set; }
    public long? GeonameId { get; set; }
}

public class ExchangeRate
{
    public long Id { get; set; }
    public int CurrencyId { get; set; }
    public Currency? Currency { get; set; }
    public decimal RateToEur { get; set; }   // 1 unit of Currency = RateToEur EUR
    public DateTime AsOf { get; set; }
}
