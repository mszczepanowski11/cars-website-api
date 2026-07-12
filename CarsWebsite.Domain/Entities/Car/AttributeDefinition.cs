using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.Domain.Entities;

public enum AttributeDataType
{
    Text, Number, Decimal, Boolean, Select, MultiSelect, Date, File
}

// Faza 2 of the category/attribute restructure (see /root/.claude/plans/crispy-riding-mochi.md):
// a category- (and optionally subtype-) scoped field definition, editable through the admin UI
// with no deploy required - this is what makes adding a category-specific field ("szerokość
// opony (mm)") a same-day operation instead of a migration + frontend release.
public class AttributeDefinition
{
    public int Id { get; set; }

    public int VehicleCategoryId { get; set; }
    public VehicleCategory VehicleCategory { get; set; } = null!;

    // Null = applies to the whole category. Set = applies only within one VehicleSubtype (mirrors
    // today's SUBTYPE_EXTRA_FIELDS scoping, e.g. "ciagnik-siodlowy"-only fields).
    public int? VehicleSubtypeId { get; set; }
    public VehicleSubtype? VehicleSubtype { get; set; }

    [MaxLength(100)] public string Key { get; set; } = string.Empty;
    [MaxLength(200)] public string LabelPl { get; set; } = string.Empty;
    public AttributeDataType DataType { get; set; }
    [MaxLength(30)] public string? Unit { get; set; }

    // Free-form JSON so validation/option shapes can evolve without a schema migration:
    // ValidationJson e.g. {"min":0,"max":500,"maxLength":100,"regex":"..."}
    // OptionsJson e.g. ["Lato","Zima","Całoroczne"] for Select/MultiSelect
    public string? ValidationJson { get; set; }
    public string? OptionsJson { get; set; }

    public bool IsRequired { get; set; }
    public bool IsFilterable { get; set; }
    public bool IsSearchable { get; set; }

    // Soft-disable only - see AdvertAttributeValue below for why this can never hard-delete once
    // referenced. Hidden from new-listing forms/filters when false; existing values stay readable.
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public ICollection<AdvertAttributeValue> Values { get; set; } = new List<AdvertAttributeValue>();
}
