using CarsWebsite;
using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.Domain.Entities;

// Flattened from the former Advert/CarAdvert TPT split (CTO audit Etap 4 - "Spłaszczyć TPT
// Advert/CarAdvert do jednej tabeli: niepotrzebny JOIN na każdym odczycie ogłoszenia; jedyny
// podtyp w całym kodzie"). CarAdvert was the only subtype ever created - nothing in this codebase
// ever inserted a bare Advert row - so the two-table split bought nothing but an extra JOIN on
// every single advert read. See migration FlattenAdvertCarAdvert for how the two tables were
// merged without losing data.
public class CarAdvert
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

    public int? VehicleCategoryId { get; set; }
    public VehicleCategory? VehicleCategory { get; set; }

    // Nullable since Faza 6 of the category/attribute restructure: non-vehicle categories
    // (Usługi motoryzacyjne) and several existing machinery categories with a free-text brand
    // field have no real Brand/FuelType to attach.
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }
    public int? ModelId { get; set; }
    public Model? Model { get; set; }
    public int? GenerationId { get; set; }
    public Generation? Generation { get; set; }
    public int? EngineVersionId { get; set; }
    public EngineVersion? EngineVersion { get; set; }
    public int? FuelTypeId { get; set; }
    public FuelType? FuelType { get; set; }
    public int? GearboxId { get; set; }
    public Gearbox? Gearbox { get; set; }
    public int? BodyTypeId { get; set; }
    public BodyType? BodyType { get; set; }
    public int? DriveTypeId { get; set; }
    public DriveType? DriveType { get; set; }
    public int? ColorId { get; set; }
    public CarColor? CarColor { get; set; }

    // Core specs
    public int Year { get; set; }
    public int Mileage { get; set; }
    public int? PowerHP { get; set; }
    public int? PowerKW { get; set; }
    public int? EngineSize { get; set; }
    public int? DoorCount { get; set; }
    public int? SeatsCount { get; set; }

    // VIN & identification
    public string? Vin { get; set; }
    [MaxLength(100)] public string? Slug { get; set; }

    // Sale info
    [MaxLength(20)] public string? Condition { get; set; }       // "new" | "used"
    public bool IsNegotiable { get; set; }
    [MaxLength(20)] public string? SellerType { get; set; }      // "private" | "dealer"

    // Vehicle history
    public DateTime? FirstRegistrationDate { get; set; }
    public string? RegistrationCountry { get; set; }
    public int? OwnersCount { get; set; }
    public bool IsImported { get; set; }
    public string? ImportCountry { get; set; }
    public DateTime? NextInspection { get; set; }
    public bool HasServiceBook { get; set; }
    public bool HasFullServiceHistory { get; set; }
    public bool HasDamage { get; set; }
    public string? DamageDescription { get; set; }
    public bool HasWarranty { get; set; }
    public DateTime? WarrantyUntil { get; set; }

    // Technical parameters
    public int? Torque { get; set; }             // Nm
    public decimal? Acceleration { get; set; }   // 0-100 km/h in seconds
    public decimal? FuelConsumptionCity { get; set; }     // l/100km
    public decimal? FuelConsumptionHighway { get; set; }  // l/100km
    public decimal? FuelConsumptionCombined { get; set; } // l/100km
    public int? Co2Emission { get; set; }        // g/km
    [MaxLength(20)] public string? EuroNorm { get; set; }        // "Euro 3" ... "Euro 6d"
    public int? CurbWeight { get; set; }         // kg
    public int? GrossWeight { get; set; }        // kg

    // Promotion badge: "TOP", "PREMIUM", "FEATURED" or null
    [MaxLength(20)] public string? Badge { get; set; }
    public DateTime? BadgeExpiresAt { get; set; }

    // FeaturedUntil mirrors BadgeExpiresAt specifically for the "FEATURED" badge type
    // and provides a dedicated column consistent with the Event.FeaturedUntil pattern.
    public DateTime? FeaturedUntil { get; set; }

    // Commercial vehicle / truck / trailer specific
    public int? AxleCount { get; set; }
    public int? Payload { get; set; }
    public decimal? CargoLength { get; set; }
    public decimal? CargoHeight { get; set; }
    public decimal? Volume { get; set; }
    public bool? HasRetarder { get; set; }
    public bool? HasTachograph { get; set; }
    public string? BodySubtype { get; set; }

    // Subtype-specific machine fields
    public int? OperatingWeightKg { get; set; }
    public int? WorkingWidthCm { get; set; }
    public decimal? MaxDiggingDepthM { get; set; }
    public int? BucketCapacityL { get; set; }
    public int? TankCapacityL { get; set; }

    // Parts specific
    public string? CatalogNumber { get; set; }
    public string? Compatibility { get; set; }
    [MaxLength(20)] public string? Side { get; set; }   // "Lewa" | "Prawa" | "Obie strony" | "Przód" | "Tył" | null
    public int? Quantity { get; set; }
    public ICollection<PartCompatibility> PartCompatibilities { get; set; } = new List<PartCompatibility>();

    // Extended taxonomy FKs
    public int? TrimId { get; set; }
    public Trim? Trim { get; set; }
    public int? VehicleSubtypeId { get; set; }
    public VehicleSubtype? VehicleSubtype { get; set; }
    public int? PartCategoryId { get; set; }
    public PartCategory? PartCategory { get; set; }
    public int? PartSubcategoryId { get; set; }
    public PartSubcategory? PartSubcategory { get; set; }
    public string? OemNumber { get; set; }
    public string? ManufacturerPartNumber { get; set; }
    public string? PartManufacturer { get; set; }

    // Premium listing fields
    public string? RegistrationPlate { get; set; }
    public bool HasVatInvoice { get; set; }
    public bool IsLeasingPossible { get; set; }
    public bool IsCreditPossible { get; set; }
    public bool IsExchangePossible { get; set; }
    public int? GearCount { get; set; }
    public bool MetallicPaint { get; set; }
    // solid | metallic | pearl | matte | bicolor | chrome | multicolor - the form collected this
    // from the start but had nowhere to save it; MetallicPaint (derived from this) was the only
    // survivor. Kept alongside MetallicPaint rather than replacing it, since existing code/filters
    // already read that bool.
    public string? ColorFinish { get; set; }
    public int? MaxTrailerWeight { get; set; }

    // Premium history fields
    public bool IsFirstOwner { get; set; }
    public bool IsServicedAtASO { get; set; }
    public bool IsGaraged { get; set; }
    public int? KeyCount { get; set; }
    public DateTime? InsuranceUntil { get; set; }
    // Deprecated (Faza 8 of the category/attribute restructure): replaced by AdvertDocument, kept
    // for one more release as read/write-compatible until Faza 9 confirms the migration and drops
    // these two columns.
    public string? YoutubeUrl { get; set; }
    public string? PdfBrochureUrl { get; set; }
    // Applies to nearly every advert, so it's a real column rather than an AttributeDefinition.
    public bool HasHomologation { get; set; }
    public string? HomologationType { get; set; }

    // Partner XML/CSV import: ExternalId is the partner's own identifier for this listing in
    // their feed, used to match "update" vs "create" on repeat imports. Both null for adverts
    // created normally through the site.
    public int? PartnerId { get; set; }
    public Partner? Partner { get; set; }
    public string? ExternalId { get; set; }

    // Cross-source duplicate detection (CTO audit Etap 2): AKOL/44FOX-style integrators likely
    // resell overlapping dealer inventory, so the same physical car can arrive via two different
    // partners (or a partner and a manual listing). Set only at CREATE time by
    // PartnerImportService.DetectDuplicateAsync - VIN match first, falling back to a fuzzy match
    // (same brand+model+year, price within 5%, mileage within 10%) only when exactly one candidate
    // exists. Never retroactively re-evaluated, never auto-hides or deletes anything: it just
    // points at the older/canonical listing so the public search can skip this one while the
    // advert itself, and an admin review of the flag, both stay intact.
    public int? DuplicateOfId { get; set; }
    public CarAdvert? DuplicateOf { get; set; }
    public string? DuplicateMatchReason { get; set; }

    public ICollection<AdvertFeature> AdvertFeatures { get; set; } = new List<AdvertFeature>();
}
