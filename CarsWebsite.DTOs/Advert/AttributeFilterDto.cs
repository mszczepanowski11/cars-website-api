namespace cars_website_api.CarsWebsite.DTOs.Advert;

// Faza 5 of the category/attribute restructure: one filter criterion against a single
// AttributeDefinition. Exactly one of the value slots below is populated per instance -
// ValueBool for Boolean fields, ValueTextIn for Select/MultiSelect (matches any of the given
// values), ValueNumberFrom/To for Number/Decimal (either bound optional, so "min only" and
// "max only" both work).
public class AttributeFilterDto
{
    public int AttributeDefinitionId { get; set; }
    public bool? ValueBool { get; set; }
    public List<string>? ValueTextIn { get; set; }
    public decimal? ValueNumberFrom { get; set; }
    public decimal? ValueNumberTo { get; set; }
}
