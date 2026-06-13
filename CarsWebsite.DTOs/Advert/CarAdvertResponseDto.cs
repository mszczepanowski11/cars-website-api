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

    // Parts specific
    public string? CatalogNumber { get; set; }
    public string? Compatibility { get; set; }

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

    public List<FeatureDto> Features { get; set; } = new();
    public List<AdvertImageDto> Images { get; set; } = new();

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
