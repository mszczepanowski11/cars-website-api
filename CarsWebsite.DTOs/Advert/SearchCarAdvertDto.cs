namespace cars_website_api.CarsWebsite.DTOs.Advert;

public class SearchCarAdvertDto
{
    public int? CategoryId { get; set; }
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

    public int? YearFrom { get; set; }
    public int? YearTo { get; set; }
    public int? MileageFrom { get; set; }
    public int? MileageTo { get; set; }
    public decimal? PriceFrom { get; set; }
    public decimal? PriceTo { get; set; }
    public int? PowerFrom { get; set; }
    public int? PowerTo { get; set; }
    public int? EngineSizeFrom { get; set; }
    public int? EngineSizeTo { get; set; }
    public int? DoorCount { get; set; }
    public int? SeatsCount { get; set; }

    public string? Condition { get; set; }
    public string? SellerType { get; set; }
    public bool? IsNegotiable { get; set; }
    public bool? HasDamage { get; set; }
    public bool? HasWarranty { get; set; }
    public bool? HasServiceBook { get; set; }
    public bool? IsImported { get; set; }
    public string? EuroNorm { get; set; }

    // Commercial / truck / trailer filters
    public int? AxleCount { get; set; }
    public int? PayloadFrom { get; set; }
    public int? PayloadTo { get; set; }
    public int? GrossWeightFrom { get; set; }
    public int? GrossWeightTo { get; set; }
    public string? BodySubtype { get; set; }
    public bool? HasRetarder { get; set; }
    public bool? HasTachograph { get; set; }

    public List<int>? FeatureIds { get; set; }
    public string? SortBy { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
