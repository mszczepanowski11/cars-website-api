namespace cars_website_api.CarsWebsite.Domain.Entities;

// One row = "this part advert fits <Brand>[/<Model>[/<Generation>]]". ModelId/GenerationId are
// nullable on purpose: a row with only BrandId set means "fits the whole brand", BrandId+ModelId
// means "fits every generation of this model", and all three set means one specific generation —
// the same nullable-as-wildcard convention already used by FeatureCategory's Brand/Model scoping.
public class PartCompatibility
{
    public int Id { get; set; }

    public int CarAdvertId { get; set; }
    public CarAdvert CarAdvert { get; set; } = null!;

    public int BrandId { get; set; }
    public Brand Brand { get; set; } = null!;
    public int? ModelId { get; set; }
    public Model? Model { get; set; }
    public int? GenerationId { get; set; }
    public Generation? Generation { get; set; }
}
