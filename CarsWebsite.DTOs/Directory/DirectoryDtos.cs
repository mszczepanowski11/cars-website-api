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
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
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
    public string? Status { get; set; }
}
