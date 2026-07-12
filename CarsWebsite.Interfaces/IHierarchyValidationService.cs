namespace cars_website_api.CarsWebsite.Interfaces;

public class ChainValidationResult
{
    public bool IsValid { get; set; } = true;
    public string? BrokenLink { get; set; }
    public string? ErrorMessage { get; set; }

    public static ChainValidationResult Ok() => new() { IsValid = true };

    public static ChainValidationResult Fail(string brokenLink, string message) =>
        new() { IsValid = false, BrokenLink = brokenLink, ErrorMessage = message };
}

public class TaxonomyAuditReport
{
    public List<string> MismatchedEngineTrimGeneration { get; set; } = new();
    public List<string> DuplicateBrandNames { get; set; } = new();
}

public interface IHierarchyValidationService
{
    // Validates that each supplied id in the Brand -> Model -> Generation -> Trim -> EngineVersion
    // chain actually belongs to its parent, and that the brand belongs to the given vehicle category.
    // Any id left null is skipped (not every category uses the full chain, e.g. czesci/rolnicze).
    Task<ChainValidationResult> ValidateVehicleChainAsync(
        int? brandId,
        int? modelId,
        int? generationId,
        int? trimId,
        int? engineVersionId,
        int? vehicleCategoryId);

    // Fail-open: a brand/name pattern with no matching rule is never rejected. Only meant to
    // catch a handful of obvious cases (e.g. an exotic petrol-only brand marked Diesel, or a
    // performance-badged trim claiming implausibly low power) — not exhaustive coverage.
    Task<ChainValidationResult> ValidateEnginePlausibilityAsync(
        int? brandId,
        int? fuelTypeId,
        int? engineVersionId,
        int? trimId,
        int? powerHP);

    Task<TaxonomyAuditReport> GetAuditReportAsync();
}
