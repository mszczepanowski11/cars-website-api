namespace cars_website_api.CarsWebsite.DTOs.Advert;

// Request shape: what the client submits when marking a part advert as fitting a vehicle.
public class PartCompatibilityEntryDto
{
    public int BrandId { get; set; }
    public int? ModelId { get; set; }
    public int? GenerationId { get; set; }
}

// Response shape: same triple, plus resolved display names.
public class PartCompatibilityDto
{
    public int Id { get; set; }
    public int BrandId { get; set; }
    public string BrandName { get; set; } = string.Empty;
    public int? ModelId { get; set; }
    public string? ModelName { get; set; }
    public int? GenerationId { get; set; }
    public string? GenerationName { get; set; }
}
