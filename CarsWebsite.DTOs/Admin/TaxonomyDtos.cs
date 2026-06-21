namespace cars_website_api.CarsWebsite.DTOs.Admin;

public class CreateFeatureDto
{
    public string Name { get; set; } = string.Empty;
    public int CategoryId { get; set; }
}

public class CreateFeatureCategoryDto
{
    public string Name { get; set; } = string.Empty;
    public int? VehicleCategoryId { get; set; }
    public int? BrandId { get; set; }
    public int? ModelId { get; set; }
}

public class CreateBrandDto
{
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public List<int>? CategoryIds { get; set; }
}

public class CreateModelDto
{
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public int BrandId { get; set; }
}

public class CreateGenerationDto
{
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public int ModelId { get; set; }
    public int YearFrom { get; set; }
    public int? YearTo { get; set; }
}

public class CreateEngineDto
{
    public string EngineName { get; set; } = string.Empty;
    public int GenerationId { get; set; }
    public int FuelTypeId { get; set; }
    public int? PowerHP { get; set; }
    public int? PowerKW { get; set; }
    public int? Displacement { get; set; }
    public decimal? FuelConsumptionCity { get; set; }
    public decimal? FuelConsumptionHighway { get; set; }
    public decimal? FuelConsumptionCombined { get; set; }
}

public class ValidateCombinationDto
{
    public int? BrandId { get; set; }
    public string? BrandName { get; set; }
    public string? FuelTypeName { get; set; }
    public string? EngineName { get; set; }
    public int? PowerHP { get; set; }
    public int? Displacement { get; set; }
}

public class AnalyzeQualityDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public int PhotoCount { get; set; }
    public bool HasBrand { get; set; }
    public bool HasModel { get; set; }
    public bool HasYear { get; set; }
    public bool HasPrice { get; set; }
    public bool HasFuelType { get; set; }
    public int FeatureCount { get; set; }
    public bool HasVin { get; set; }
}
