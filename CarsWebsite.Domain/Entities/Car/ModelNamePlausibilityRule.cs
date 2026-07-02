namespace cars_website_api.CarsWebsite.Domain.Entities;

// Matched as a case-insensitive substring against an EngineVersion's EngineName or a Trim's Name
// (e.g. NamePattern "M3" catches EngineVersion.EngineName "M3 Competition"). Whichever one matches
// must claim at least MinPowerHP — this is a scalable structure for "an M3-badged version can't
// claim 90 KM" rather than a hardcoded list, since there's no separate performance-sub-brand
// entity anywhere in this schema.
public class ModelNamePlausibilityRule
{
    public int Id { get; set; }
    public string NamePattern { get; set; } = string.Empty;
    public int MinPowerHP { get; set; }
    public string? Description { get; set; }
}
