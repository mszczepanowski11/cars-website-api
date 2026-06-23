namespace cars_website_api.CarsWebsite.DTOs.Car;

public class EngineVersionDto
{
    public int Id { get; set; }
    public string EngineName { get; set; }
    public string Name { get; set; }
    public int? PowerHP { get; set; }
    public int? Horsepower { get; set; }
    public int? PowerKW { get; set; }
    public int? Displacement { get; set; }
    public decimal? FuelConsumptionCity { get; set; }
    public decimal? FuelConsumptionHighway { get; set; }
    public decimal? FuelConsumptionCombined { get; set; }
    public int? FuelTypeId { get; set; }
    public string? FuelTypeName { get; set; }
    public int? TrimId { get; set; }
    public int? TorqueNm { get; set; }
    public int? Co2EmissionGkm { get; set; }
    public string? EuroNorm { get; set; }
    public decimal? Acceleration0100 { get; set; }
    public int? TopSpeedKmh { get; set; }
    public string? DriveType { get; set; }
    public string? GearboxType { get; set; }
    public int? Cylinders { get; set; }
}