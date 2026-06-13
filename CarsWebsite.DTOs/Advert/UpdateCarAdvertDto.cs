namespace cars_website_api.CarsWebsite.DTOs.Advert;

public class UpdateCarAdvertDto
{
    public int? VehicleCategoryId { get; set; }
    public int BrandId { get; set; }
    public int ModelId { get; set; }
    public int? GenerationId { get; set; }
    public int? EngineVersionId { get; set; }

    public int FuelTypeId { get; set; }
    public int? GearboxId { get; set; }
    public int? BodyTypeId { get; set; }
    public int? DriveTypeId { get; set; }
    public int? ColorId { get; set; }

    public int Year { get; set; }
    public int Mileage { get; set; }
    public decimal Price { get; set; }
    public bool IsNegotiable { get; set; }
    public string? SellerType { get; set; }
    public string? Condition { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
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

    public List<int>? FeatureIds { get; set; }
}
