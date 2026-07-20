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
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
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
    public string? Status { get; set; }
}
