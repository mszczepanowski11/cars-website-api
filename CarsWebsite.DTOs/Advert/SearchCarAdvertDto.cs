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
    public int? YearFrom { get; set; }
    public int? YearTo { get; set; }
    public int? MileageFrom { get; set; }
    public int? MileageTo { get; set; }
    public decimal? PriceFrom { get; set; }
    public decimal? PriceTo { get; set; }
    public List<int>? FeatureIds { get; set; }
    public string? SortBy { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}