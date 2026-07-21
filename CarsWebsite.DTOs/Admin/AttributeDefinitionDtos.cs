using cars_website_api.CarsWebsite.Domain.Entities;

namespace cars_website_api.CarsWebsite.DTOs.Admin;

public class AttributeDefinitionDto
{
    public int Id { get; set; }
    public int VehicleCategoryId { get; set; }
    public string? VehicleCategoryName { get; set; }
    public int? VehicleSubtypeId { get; set; }
    public string? VehicleSubtypeName { get; set; }
    public int? BrandId { get; set; }
    public string? BrandName { get; set; }
    public int? ModelId { get; set; }
    public string? ModelName { get; set; }
    public int? GenerationId { get; set; }
    public string? GenerationName { get; set; }
    public int? TrimId { get; set; }
    public string? TrimName { get; set; }
    public string Key { get; set; } = string.Empty;
    public string LabelPl { get; set; } = string.Empty;
    public AttributeDataType DataType { get; set; }
    public string? Unit { get; set; }
    public string? ValidationJson { get; set; }
    public string? OptionsJson { get; set; }
    public bool IsRequired { get; set; }
    public bool IsFilterable { get; set; }
    public bool IsSearchable { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public int UsageCount { get; set; }
}

public class CreateAttributeDefinitionDto
{
    public int VehicleCategoryId { get; set; }
    public int? VehicleSubtypeId { get; set; }
    public int? BrandId { get; set; }
    public int? ModelId { get; set; }
    public int? GenerationId { get; set; }
    public int? TrimId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string LabelPl { get; set; } = string.Empty;
    public AttributeDataType DataType { get; set; }
    public string? Unit { get; set; }
    public string? ValidationJson { get; set; }
    public string? OptionsJson { get; set; }
    public bool IsRequired { get; set; }
    public bool IsFilterable { get; set; }
    public bool IsSearchable { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

// One value the seller entered for one AttributeDefinition on one advert - the shape Faza 3's
// add-advert.vue submit posts, replacing today's buildDescription() text-serialization.
public class AdvertAttributeValueDto
{
    public int AttributeDefinitionId { get; set; }
    public string? ValueText { get; set; }
    public decimal? ValueNumber { get; set; }
    public bool? ValueBool { get; set; }
    public DateTime? ValueDate { get; set; }
}
