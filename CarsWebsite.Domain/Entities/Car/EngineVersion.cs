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

    public int? TrimId { get; set; }
    public Trim? Trim { get; set; }
    public int? TorqueNm { get; set; }
    public int? Co2EmissionGkm { get; set; }
    public string? EuroNorm { get; set; }
    public decimal? AvgConsumptionL { get; set; }
    public decimal? Acceleration0100 { get; set; }
    public int? TopSpeedKmh { get; set; }
    public string? DriveType { get; set; } // "FWD", "RWD", "AWD", "4WD"
    public string? GearboxType { get; set; } // "manual", "automatic", "dct", "cvt"
    public int? Cylinders { get; set; }

    public Generation Generation { get; set; }
    public FuelType FuelType { get; set; }
}