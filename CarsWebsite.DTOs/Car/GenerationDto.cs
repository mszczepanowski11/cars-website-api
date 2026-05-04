namespace cars_website_api.CarsWebsite.DTOs.Car;

public class GenerationDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int YearFrom { get; set; }
    public int? YearTo { get; set; }
}