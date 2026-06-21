namespace cars_website_api.CarsWebsite.DTOs.Advert;

public class AiDescriptionRequestDto
{
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Generation { get; set; }
    public int? Year { get; set; }
    public int? Mileage { get; set; }
    public string? FuelType { get; set; }
    public int? PowerHP { get; set; }
    public int? EngineCapacity { get; set; }
    public string? Gearbox { get; set; }
    public bool HasServiceBook { get; set; }
    public bool HasFullServiceHistory { get; set; }
    public int? OwnersCount { get; set; }
    public string? Condition { get; set; }
    public int FeaturesCount { get; set; }
}

internal class AnthropicResponse
{
    public List<AnthropicContent>? Content { get; set; }
}

internal class AnthropicContent
{
    public string? Text { get; set; }
}
