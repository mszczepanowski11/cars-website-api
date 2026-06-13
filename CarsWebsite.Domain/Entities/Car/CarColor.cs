namespace cars_website_api.CarsWebsite.Domain.Entities;

public class CarColor
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? HexCode { get; set; }
}
