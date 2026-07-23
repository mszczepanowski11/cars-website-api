namespace cars_website_api.CarsWebsite.DTOs.Directory;

// Public list/card shape - only the fields a directory listing needs.
public class DirectoryCompanyListDto
{
    public string PublicId { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
    public string? City { get; set; }
    public string? Website { get; set; }
    public string Status { get; set; } = string.Empty;
}

// Full profile shape for /firmy/{slug}.
public class DirectoryCompanyDetailDto : DirectoryCompanyListDto
{
    public string? Address { get; set; }
    public string? PostalCode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? ProfileUrl { get; set; }
    public string? Language { get; set; }
    public string? Description { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool Linked { get; set; }
    // Languages this company can be viewed in (base Language + every key in the I18n JSON).
    public List<string> AvailableLanguages { get; set; } = new();
    // Contact languages the company can be reached in (Etap 4), distinct from AvailableLanguages
    // above (which is about which languages the profile TEXT is translated into).
    public List<string> ContactLanguages { get; set; } = new();
    // Multiple locations (Etap 4 of the globalization roadmap). Empty for companies that haven't
    // been migrated to structured branches yet - callers should fall back to Address/Phone above.
    public List<CompanyBranchDto> Branches { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CompanyBranchDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public bool IsPrimary { get; set; }
    public string? CountryIso2 { get; set; }
    public string? RegionName { get; set; }
    public string? CityName { get; set; }
    public string? PostalCode { get; set; }
    public string? AddressLine { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? TimeZone { get; set; }
    public List<CompanyPhoneDto> Phones { get; set; } = new();
    public List<CompanyOpeningHourDto> OpeningHours { get; set; } = new();
}

public class CompanyPhoneDto
{
    public string Number { get; set; } = string.Empty;
    public string? Label { get; set; }
}

public class CompanyOpeningHourDto
{
    public DayOfWeek DayOfWeek { get; set; }
    public bool IsClosed { get; set; }
    public TimeSpan? OpenTime { get; set; }
    public TimeSpan? CloseTime { get; set; }
}

// Admin create/update payload for a single branch. Phones/OpeningHours are replaced wholesale on
// each write, same convention AdvertService uses for AdvertFeatures/Compatibilities.
public class CompanyBranchInputDto
{
    public string? Name { get; set; }
    public bool IsPrimary { get; set; }
    public int? CountryId { get; set; }
    public int? RegionId { get; set; }
    public long? CityId { get; set; }
    public string? PostalCode { get; set; }
    public string? AddressLine { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? TimeZoneId { get; set; }
    public List<CompanyPhoneDto> Phones { get; set; } = new();
    public List<CompanyOpeningHourDto> OpeningHours { get; set; } = new();
}

// One language's translation of the localizable fields.
public class LocalizedTextDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
}

// Lightweight point for the company map (blueprint: geo layer).
public class DirectoryMapPointDto
{
    public string PublicId { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? City { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

// A single advert card shown on a company profile (graph edge Firma -> Ogłoszenia).
public class DirectoryListingCardDto
{
    public int Id { get; set; }
    public string? Slug { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "PLN";
    public int? Year { get; set; }
    public int? Mileage { get; set; }
    public string? BrandName { get; set; }
    public string? ModelName { get; set; }
    public string? ImageUrl { get; set; }
    public string? Badge { get; set; }
}

public class DirectoryListingsResultDto
{
    public List<DirectoryListingCardDto> Items { get; set; } = new();
    public int Total { get; set; }
    // False when the company isn't linked to a Carizo account yet (nothing to show).
    public bool Linked { get; set; }
}

public class DirectoryListResultDto
{
    public List<DirectoryCompanyListDto> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class DirectoryFacetDto
{
    public string Value { get; set; } = string.Empty;
    public int Count { get; set; }
}

// One row in a bulk import batch (first ingestion connector, blueprint section 10).
public class DirectoryImportRowDto
{
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? CountryCode { get; set; }
    public string? City { get; set; }
    public string? Address { get; set; }
    public string? PostalCode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public class DirectoryImportRequestDto
{
    public List<DirectoryImportRowDto> Rows { get; set; } = new();
    // Provenance tag stored on every created row, e.g. "import:ceidg" or "import:manual".
    public string? Source { get; set; }
    // Default category when a row omits its own.
    public string? DefaultCategory { get; set; }
}

public class DirectoryImportResultDto
{
    public int Received { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public List<string> Notes { get; set; } = new();
}

// Admin create/update payload.
public class DirectoryCompanyInputDto
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
    public string? City { get; set; }
    public string? Address { get; set; }
    public string? PostalCode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? ProfileUrl { get; set; }
    public string? Language { get; set; }
    public string? Description { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    // Manual translations from the admin panel: {"de": {name, description}, ...}.
    public Dictionary<string, LocalizedTextDto>? I18n { get; set; }
    public string? Status { get; set; }
}
