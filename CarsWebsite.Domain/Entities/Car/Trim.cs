namespace cars_website_api.CarsWebsite.Domain.Entities;

public class Trim
{
    public int Id { get; set; }
    public int GenerationId { get; set; }
    public Generation Generation { get; set; } = null!;
    public string Name { get; set; } = string.Empty; // e.g. "Sport", "Luxury", "Basic"
    public string? Description { get; set; }

    public ICollection<EngineVersion> EngineVersions { get; set; } = new List<EngineVersion>();
}
