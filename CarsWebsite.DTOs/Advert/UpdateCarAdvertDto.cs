using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs.Advert;

public class UpdateCarAdvertDto
{
    public int? VehicleCategoryId { get; set; }
    public int? VehicleSubtypeId { get; set; }
    public int? BrandId { get; set; }
    public int? ModelId { get; set; }
    public int? GenerationId { get; set; }
    public int? EngineVersionId { get; set; }
    public int? TrimId { get; set; }

    public int? FuelTypeId { get; set; }
    public int? GearboxId { get; set; }
    public int? BodyTypeId { get; set; }
    public int? DriveTypeId { get; set; }
    public int? ColorId { get; set; }

    [Range(1900, 2030, ErrorMessage = "Rok musi być między 1900 a 2030.")]
    public int Year { get; set; }

    [Range(0, 2_000_000, ErrorMessage = "Przebieg musi być między 0 a 2 000 000.")]
    public int Mileage { get; set; }

    [Range(0, 10_000_000, ErrorMessage = "Cena musi być między 0 a 10 000 000.")]
    public decimal Price { get; set; }

    public bool IsNegotiable { get; set; }
    [MaxLength(20)] public string? SellerType { get; set; }
    [MaxLength(20)] public string? Condition { get; set; }

    [Required]
    [MaxLength(200, ErrorMessage = "Tytuł nie może przekraczać 200 znaków.")]
    public string Title { get; set; } = string.Empty;

    [MaxLength(5000)]
    public string? Description { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? Region { get; set; }

    [MaxLength(17)]
    [RegularExpression(@"^[A-HJ-NPR-Z0-9]{17}$", ErrorMessage = "VIN musi mieć dokładnie 17 znaków alfanumerycznych (bez liter I, O, Q).")]
    public string? Vin { get; set; }

    [Range(1, 10)] public int? DoorCount { get; set; }
    [Range(1, 100)] public int? SeatsCount { get; set; }

    // Technical
    [Range(1, 5000)] public int? PowerHP { get; set; }
    [Range(1, 4000)] public int? PowerKW { get; set; }
    [Range(1, 100000)] public int? EngineSize { get; set; }
    [Range(1, 5000)] public int? Torque { get; set; }
    [Range(0, 60)] public decimal? Acceleration { get; set; }
    [Range(0, 100)] public decimal? FuelConsumptionCity { get; set; }
    [Range(0, 100)] public decimal? FuelConsumptionHighway { get; set; }
    [Range(0, 100)] public decimal? FuelConsumptionCombined { get; set; }
    [Range(0, 2000)] public int? Co2Emission { get; set; }
    [MaxLength(20)] public string? EuroNorm { get; set; }
    [Range(100, 100000)] public int? CurbWeight { get; set; }
    [Range(100, 200000)] public int? GrossWeight { get; set; }

    // Vehicle history
    public DateTime? FirstRegistrationDate { get; set; }
    [MaxLength(100)] public string? RegistrationCountry { get; set; }
    [Range(0, 100)] public int? OwnersCount { get; set; }
    public bool IsImported { get; set; }
    [MaxLength(100)] public string? ImportCountry { get; set; }
    public DateTime? NextInspection { get; set; }
    public bool HasServiceBook { get; set; }
    public bool HasFullServiceHistory { get; set; }
    public bool HasDamage { get; set; }
    [MaxLength(2000)] public string? DamageDescription { get; set; }
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
    [MaxLength(100)] public string? BodySubtype { get; set; }

    // Parts specific
    [MaxLength(100)] public string? CatalogNumber { get; set; }
    [MaxLength(1000)] public string? Compatibility { get; set; }
    [MaxLength(20)] public string? Side { get; set; }
    [Range(1, 100000)] public int? Quantity { get; set; }
    public int? PartCategoryId { get; set; }
    public int? PartSubcategoryId { get; set; }
    [MaxLength(100)] public string? OemNumber { get; set; }
    [MaxLength(100)] public string? ManufacturerPartNumber { get; set; }
    [MaxLength(100)] public string? PartManufacturer { get; set; }
    public List<PartCompatibilityEntryDto>? Compatibilities { get; set; }

    // Subtype-specific machine fields
    public int? OperatingWeightKg { get; set; }
    public int? WorkingWidthCm { get; set; }
    public decimal? MaxDiggingDepthM { get; set; }
    public int? BucketCapacityL { get; set; }
    public int? TankCapacityL { get; set; }

    public List<int>? FeatureIds { get; set; }

    // Faza 3 of the category/attribute restructure: category-specific fields sourced from
    // AttributeDefinition instead of the old extraFields-into-description text dump.
    public List<cars_website_api.CarsWebsite.DTOs.Admin.AdvertAttributeValueDto>? AttributeValues { get; set; }

    // Premium listing fields
    [MaxLength(20)] public string? RegistrationPlate { get; set; }
    public bool HasVatInvoice { get; set; }
    public bool IsLeasingPossible { get; set; }
    public bool IsCreditPossible { get; set; }
    public bool IsExchangePossible { get; set; }
    [Range(1, 20)] public int? GearCount { get; set; }
    public bool MetallicPaint { get; set; }
    [Range(0, 100000)] public int? MaxTrailerWeight { get; set; }
    public bool IsFirstOwner { get; set; }
    public bool IsServicedAtASO { get; set; }
    public bool IsGaraged { get; set; }
    [Range(1, 10)] public int? KeyCount { get; set; }
    public DateTime? InsuranceUntil { get; set; }
    [MaxLength(500)] public string? YoutubeUrl { get; set; }
    [MaxLength(1000)] public string? PdfBrochureUrl { get; set; }
}
