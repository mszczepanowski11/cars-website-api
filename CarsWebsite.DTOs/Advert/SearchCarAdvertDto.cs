using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs.Advert;

public class SearchCarAdvertDto
{
    public int? CategoryId { get; set; }
    public int? UserId { get; set; }

    [MaxLength(150)]
    public string? TextSearch { get; set; }

    public int? BrandId { get; set; }
    public int? ModelId { get; set; }
    public int? GenerationId { get; set; }
    public int? EngineVersionId { get; set; }
    public int? FuelTypeId { get; set; }
    public int? GearboxId { get; set; }
    public int? BodyTypeId { get; set; }
    public int? DriveTypeId { get; set; }
    public int? ColorId { get; set; }
    public int? VehicleSubtypeId { get; set; }

    [Range(1900, 2100)]
    public int? YearFrom { get; set; }

    [Range(1900, 2100)]
    public int? YearTo { get; set; }

    [Range(0, 5_000_000)]
    public int? MileageFrom { get; set; }

    [Range(0, 5_000_000)]
    public int? MileageTo { get; set; }

    [Range(0, 100_000_000)]
    public decimal? PriceFrom { get; set; }

    [Range(0, 100_000_000)]
    public decimal? PriceTo { get; set; }

    [Range(0, 10_000)]
    public int? PowerFrom { get; set; }

    [Range(0, 10_000)]
    public int? PowerTo { get; set; }

    [Range(0, 100_000)]
    public int? EngineSizeFrom { get; set; }

    [Range(0, 100_000)]
    public int? EngineSizeTo { get; set; }

    [Range(1, 10)]
    public int? DoorCount { get; set; }

    [Range(1, 50)]
    public int? SeatsCount { get; set; }

    [MaxLength(50)]
    public string? Condition { get; set; }

    [MaxLength(50)]
    public string? SellerType { get; set; }

    public bool? IsNegotiable { get; set; }
    public bool? HasDamage { get; set; }
    public bool? HasWarranty { get; set; }
    public bool? HasServiceBook { get; set; }
    public bool? IsImported { get; set; }

    [MaxLength(10)]
    public string? EuroNorm { get; set; }

    // Commercial / truck / trailer filters
    [Range(0, 20)]
    public int? AxleCount { get; set; }

    [Range(0, 1_000_000)]
    public int? PayloadFrom { get; set; }

    [Range(0, 1_000_000)]
    public int? PayloadTo { get; set; }

    [Range(0, 1_000_000)]
    public int? GrossWeightFrom { get; set; }

    [Range(0, 1_000_000)]
    public int? GrossWeightTo { get; set; }

    [MaxLength(100)]
    public string? BodySubtype { get; set; }

    public bool? HasRetarder { get; set; }
    public bool? HasTachograph { get; set; }

    // Parts specific
    [MaxLength(100)]
    public string? CatalogNumber { get; set; }

    public int? PartCategoryId { get; set; }
    public int? PartSubcategoryId { get; set; }

    [MaxLength(20)]
    public string? Side { get; set; }

    public int? QuantityFrom { get; set; }

    [MaxLength(100)]
    public string? OemNumber { get; set; }

    [MaxLength(100)]
    public string? ManufacturerPartNumber { get; set; }

    [MaxLength(100)]
    public string? PartManufacturer { get; set; }

    // Filters parts advertised as fitting this vehicle — a PartCompatibility row with a null
    // ModelId/GenerationId is a wildcard, so e.g. a "fits all BMW" row matches any BMW model.
    public int? CompatibleBrandId { get; set; }
    public int? CompatibleModelId { get; set; }
    public int? CompatibleGenerationId { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? Region { get; set; }

    // Premium filters
    public bool? HasVatInvoice { get; set; }
    public bool? IsExchangePossible { get; set; }
    public bool? IsLeasingPossible { get; set; }
    public bool? IsCreditPossible { get; set; }
    public bool? MetallicPaint { get; set; }
    public bool? IsFirstOwner { get; set; }
    public bool? IsGaraged { get; set; }

    [MaxLength(50)]
    public List<int>? FeatureIds { get; set; }

    [MaxLength(50)]
    public string? SortBy { get; set; }

    [Range(1, 10_000)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}
