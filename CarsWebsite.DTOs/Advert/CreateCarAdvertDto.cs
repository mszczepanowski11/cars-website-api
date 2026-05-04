namespace cars_website_api.CarsWebsite.DTOs.Advert;

public class CreateCarAdvertDto
{
    public int BrandId { get; set; }
    public int ModelId { get; set; }
    public int GenerationId { get; set; }
    public int EngineVersionId { get; set; }

    public int FuelTypeId { get; set; }
    public int GearboxId { get; set; }
    public int BodyTypeId { get; set; }

    public int Year { get; set; }
    public int Mileage { get; set; }
    public decimal Price { get; set; }

    public string Title { get; set; }
    public string Description { get; set; }

    public List<int> FeatureIds { get; set; }
}