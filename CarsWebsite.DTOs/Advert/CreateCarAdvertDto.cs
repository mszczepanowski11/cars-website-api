using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs.Advert;

public class CreateCarAdvertDto
{
    public int? VehicleCategoryId { get; set; }
    public int? VehicleSubtypeId { get; set; }
    public int BrandId { get; set; }
    public int ModelId { get; set; }
    public int? GenerationId { get; set; }
    public int? EngineVersionId { get; set; }
    public int? TrimId { get; set; }

    public int FuelTypeId { get; set; }
    public int? GearboxId { get; set; }
    public int? BodyTypeId { get; set; }
    public int? DriveTypeId { get; set; }
    public int? ColorId { get; set; }

    [Range(1900, 2030)] public int Year { get; set; }
    [Range(0, 2000000)] public int Mileage { get; set; }
    [Range(0, 10000000)] public decimal Price { get; set; }
    public bool IsNegotiable { get; set; }
    public string? SellerType { get; set; }
    public string? Condition { get; set; }

    [Required] [MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(5000)] public string? Description { get; set; }
    [MaxLength(100)] public string? City { get; set; }
    [MaxLength(100)] public string? Region { get; set; }
    [MaxLength(17)]
    [RegularExpression(@"^[A-HJ-NPR-Z0-9]{17}$", ErrorMessage = "VIN musi mieć dokładnie 17 znaków alfanumerycznych (bez liter I, O, Q).")]
    public string? Vin { get; set; }
    public int? DoorCount { get; set; }
    public int? SeatsCount { get; set; }

    // Technical
    public int? PowerHP { get; set; }
    public int? PowerKW { get; set; }
    public int? EngineSize { get; set; }
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

    // Parts specific
    public string? CatalogNumber { get; set; }
    public string? Compatibility { get; set; }

    // Subtype-specific machine fields
    public int? OperatingWeightKg { get; set; }
    public int? WorkingWidthCm { get; set; }
    public decimal? MaxDiggingDepthM { get; set; }
    public int? BucketCapacityL { get; set; }
    public int? TankCapacityL { get; set; }

    public List<int>? FeatureIds { get; set; }

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
