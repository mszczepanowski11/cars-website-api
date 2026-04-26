namespace cars_website_api.CarsWebsite.Domain.Entities;

public class EngineVersion
{
    public int Id { get; set; }
    public int GenerationId { get; set; }
    public int FuelTypeId { get; set; }

    public string EngineName { get; set; } 
    public int? PowerHP { get; set; }
    public int? PowerKW { get; set; }
    public int? Displacement { get; set; }

    public Generation Generation { get; set; }
    public FuelType FuelType { get; set; }
}