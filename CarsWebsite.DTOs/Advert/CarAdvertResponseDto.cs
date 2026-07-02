using cars_website_api.CarsWebsite.DTOs.Car;

namespace cars_website_api.CarsWebsite.DTOs.Advert;

public class CarAdvertResponseDto
{
    public int Id { get; set; }
    public int UserId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Slug { get; set; }

    public decimal Price { get; set; }
    public string Currency { get; set; } = "PLN";
    public bool IsNegotiable { get; set; }
    public string? SellerType { get; set; }
    public string? Condition { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public bool IsActive { get; set; }
    public bool IsHidden { get; set; }
    public DateTime? ExpiresAt { get; set; }

    // Core specs
    public int Year { get; set; }
    public int Mileage { get; set; }
    public int? PowerHP { get; set; }
    public int? PowerKW { get; set; }
    public int? EngineSize { get; set; }
    public int? DoorCount { get; set; }
    public int? SeatsCount { get; set; }

    // VIN
    public string? Vin { get; set; }

    // Promotion badge
    public string? Badge { get; set; }
    public DateTime? BadgeExpiresAt { get; set; }
    public int ViewCount { get; set; }
    public DateTime? SoldAt { get; set; }

    // Technical params
    public int? Torque { get; set; }
    public decimal? Acceleration { get; set; }
    public decimal? FuelConsumptionCity { get; set; }
    public decimal? FuelConsumptionHighway { get; set; }
    public decimal? FuelConsumptionCombined { get; set; }
    public int? Co2Emission { get; set; }
    public string? EuroNorm { get; set; }
    public int? CurbWeight { get; set; }
    public int? GrossWeight { get; set; }

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

    // Commercial / truck / trailer specific
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
    public string? Side { get; set; }
    public int? Quantity { get; set; }
    public int? PartCategoryId { get; set; }
    public string? PartCategoryName { get; set; }
    public int? PartSubcategoryId { get; set; }
    public string? PartSubcategoryName { get; set; }
    public string? OemNumber { get; set; }
    public string? ManufacturerPartNumber { get; set; }
    public string? PartManufacturer { get; set; }
    public List<PartCompatibilityDto> Compatibilities { get; set; } = new();

    // Vehicle subtype
    public int? VehicleSubtypeId { get; set; }
    public string? VehicleSubtypeName { get; set; }

    // Taxonomy
    public BrandDto Brand { get; set; } = null!;
    public ModelDto Model { get; set; } = null!;
    public GenerationDto? Generation { get; set; }
    public EngineVersionDto? EngineVersion { get; set; }
    public FuelTypeDto FuelType { get; set; } = null!;
    public GearboxDto? Gearbox { get; set; }
    public BodyTypeDto? BodyType { get; set; }
    public DriveTypeDto? DriveType { get; set; }
    public CarColorDto? Color { get; set; }

    // Premium listing fields
    public string? RegistrationPlate { get; set; }
    public bool HasVatInvoice { get; set; }
    public bool IsLeasingPossible { get; set; }
    public bool IsCreditPossible { get; set; }
    public bool IsExchangePossible { get; set; }
    public int? GearCount { get; set; }
    public bool MetallicPaint { get; set; }
    public int? MaxTrailerWeight { get; set; }
    public bool IsFirstOwner { get; set; }
    public bool IsServicedAtASO { get; set; }
    public bool IsGaraged { get; set; }
    public int? KeyCount { get; set; }
    public DateTime? InsuranceUntil { get; set; }
    public string? YoutubeUrl { get; set; }
    public string? PdfBrochureUrl { get; set; }

    public List<FeatureDto> Features { get; set; } = new();
    public List<AdvertImageDto> Images { get; set; } = new();

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
