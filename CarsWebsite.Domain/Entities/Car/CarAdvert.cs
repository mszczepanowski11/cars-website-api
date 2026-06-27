using CarsWebsite;

namespace cars_website_api.CarsWebsite.Domain.Entities;

public class CarAdvert : Advert
{
    public int? VehicleCategoryId { get; set; }
    public VehicleCategory? VehicleCategory { get; set; }

    public int BrandId { get; set; }
    public Brand Brand { get; set; } = null!;
    public int ModelId { get; set; }
    public Model Model { get; set; } = null!;
    public int? GenerationId { get; set; }
    public Generation? Generation { get; set; }
    public int? EngineVersionId { get; set; }
    public EngineVersion? EngineVersion { get; set; }
    public int FuelTypeId { get; set; }
    public FuelType FuelType { get; set; } = null!;
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
    public string? Slug { get; set; }

    // Sale info
    public string? Condition { get; set; }       // "new" | "used"
    public bool IsNegotiable { get; set; }
    public string? SellerType { get; set; }      // "private" | "dealer"

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
    public string? EuroNorm { get; set; }        // "Euro 3" ... "Euro 6d"
    public int? CurbWeight { get; set; }         // kg
    public int? GrossWeight { get; set; }        // kg

    // Promotion badge: "TOP", "PREMIUM", "FEATURED" or null
    public string? Badge { get; set; }
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
    public int? MaxTrailerWeight { get; set; }

    // Premium history fields
    public bool IsFirstOwner { get; set; }
    public bool IsServicedAtASO { get; set; }
    public bool IsGaraged { get; set; }
    public int? KeyCount { get; set; }
    public DateTime? InsuranceUntil { get; set; }
    public string? YoutubeUrl { get; set; }
    public string? PdfBrochureUrl { get; set; }

    public ICollection<AdvertFeature> AdvertFeatures { get; set; } = new List<AdvertFeature>();
}
