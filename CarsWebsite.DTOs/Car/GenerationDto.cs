namespace cars_website_api.CarsWebsite.DTOs.Car;

public class GenerationDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int YearFrom { get; set; }
    public int? YearTo { get; set; }

    // Physical dimensions (audit §5/§6) - shared by every advert of this generation.
    public int? LengthMm { get; set; }
    public int? WidthMm { get; set; }
    public int? HeightMm { get; set; }
    public int? WheelbaseMm { get; set; }
    public int? TrunkCapacityL { get; set; }
    public int? DefaultSeatsCount { get; set; }
    public int? DefaultDoorsCount { get; set; }
}