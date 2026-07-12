using CarsWebsite;

namespace cars_website_api.CarsWebsite.Domain.Entities;

// One row per advert per attribute actually set. Deliberately FK'd to the base Advert table (not
// CarAdvert) so this same mechanism works for the non-vehicle categories added in Faza 6 (Usługi
// motoryzacyjne has no CarAdvert row at all).
//
// Separate typed nullable columns (not one stringly-typed value) so numeric/date filtering stays
// indexable - see AppDbContext for the composite indexes this depends on. Exactly one of
// ValueText/ValueNumber/ValueBool/ValueDate is populated per row, matching AttributeDefinition.DataType.
public class AdvertAttributeValue
{
    public int Id { get; set; }

    public int AdvertId { get; set; }
    public Advert Advert { get; set; } = null!;

    public int AttributeDefinitionId { get; set; }
    public AttributeDefinition AttributeDefinition { get; set; } = null!;

    public string? ValueText { get; set; }
    public decimal? ValueNumber { get; set; }
    public bool? ValueBool { get; set; }
    public DateTime? ValueDate { get; set; }
}
