namespace CarsWebsite;

public class Advert
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "PLN";       // ISO cache (denormalized from CurrencyId)
    public string? City { get; set; }                   // display cache (denormalized from CityId)
    public string? Region { get; set; }                 // display cache (denormalized from RegionId)

    // ── Global location (Etap 1/3): structured references to the geo reference tables. The free-text
    // City/Region above are kept as a denormalized display cache + backward compatibility. ──
    public int? CountryId { get; set; }
    public int? RegionId { get; set; }
    public long? CityId { get; set; }
    public string? PostalCode { get; set; }
    public string? AddressLine { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // ── Currency (Etap 3): seller's original currency + canonical EUR for cross-market sort/filter. ──
    public int? CurrencyId { get; set; }
    public decimal? PriceEur { get; set; }
    public DateTime? PriceEurAsOf { get; set; }

    // Content language of Title/Description (i18n translation pipeline) + optional timezone.
    public int? SourceLanguageId { get; set; }
    public int? TimeZoneId { get; set; }

    public int UserId { get; set; }
    public User createdBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsHidden { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? SoldAt { get; set; }
    public ICollection<AdvertImage> Images { get; set; }
    public ICollection<AdvertDocument> Documents { get; set; } = new List<AdvertDocument>();
}