using System.ComponentModel.DataAnnotations;
using CarsWebsite;
using cars_website_api.CarsWebsite.Domain.Entities;

namespace cars_website_api.CarsWebsite.Domain.Entities.Directory;

// Etap 4 of the globalization roadmap: a DirectoryCompany can have multiple physical locations.
// The single City/Address/Latitude/Longitude/CountryCode fields on DirectoryCompany stay as the
// "primary location" display cache (mirrors the primary branch below) - same convention as
// Advert's free-text City/Region staying alongside its structured CountryId/RegionId/CityId.
public class CompanyBranch
{
    public int Id { get; set; }
    public int DirectoryCompanyId { get; set; }
    public DirectoryCompany? DirectoryCompany { get; set; }

    // null = unnamed/main branch. Set for secondary locations, e.g. "Oddział Kraków".
    [MaxLength(120)] public string? Name { get; set; }
    public bool IsPrimary { get; set; }

    public int? CountryId { get; set; }
    public Country? Country { get; set; }
    public int? RegionId { get; set; }
    public Region? Region { get; set; }
    public long? CityId { get; set; }
    public City? City { get; set; }
    [MaxLength(20)] public string? PostalCode { get; set; }
    [MaxLength(250)] public string? AddressLine { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? TimeZoneId { get; set; }
    public AppTimeZone? TimeZone { get; set; }

    public ICollection<CompanyPhone> Phones { get; set; } = new List<CompanyPhone>();
    public ICollection<CompanyOpeningHour> OpeningHours { get; set; } = new List<CompanyOpeningHour>();
}

public class CompanyPhone
{
    public int Id { get; set; }
    public int CompanyBranchId { get; set; }
    public CompanyBranch? Branch { get; set; }

    [MaxLength(40)] public string Number { get; set; } = string.Empty; // E.164
    // null = general line. e.g. "Sprzedaż", "Serwis", "Części".
    [MaxLength(40)] public string? Label { get; set; }
}

public class CompanyOpeningHour
{
    public int Id { get; set; }
    public int CompanyBranchId { get; set; }
    public CompanyBranch? Branch { get; set; }

    public DayOfWeek DayOfWeek { get; set; }
    public bool IsClosed { get; set; }
    public TimeSpan? OpenTime { get; set; }
    public TimeSpan? CloseTime { get; set; }
}

// Languages a company can be reached/served in - company-wide (not per-branch), distinct from
// DirectoryCompany.Language (the base content language of Name/Description).
public class CompanyLanguage
{
    public int Id { get; set; }
    public int DirectoryCompanyId { get; set; }
    public DirectoryCompany? DirectoryCompany { get; set; }
    public int LanguageId { get; set; }
    public Language? Language { get; set; }
}
