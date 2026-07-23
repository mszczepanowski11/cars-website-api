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
    public decimal? FuelConsumptionCity { get; set; }
    public decimal? FuelConsumptionHighway { get; set; }
    public decimal? FuelConsumptionCombined { get; set; }

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
    // Manufacturer's internal engine code (e.g. "M57D30"), distinct from EngineName (the
    // marketing/trim label, e.g. "530d 3.0d 231KM") - audit §5. Two engines in the same
    // generation can share an EngineName pattern but not this code, and the same code can recur
    // across generations/models, which is exactly what makes it useful for cross-model matching.
    public string? EngineCode { get; set; }

    public Generation Generation { get; set; }
    public FuelType FuelType { get; set; }
}