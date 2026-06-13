using cars_website_api.CarsWebsite.Domain.Entities;
using DriveType = cars_website_api.CarsWebsite.Domain.Entities.DriveType;

namespace cars_website_api.CarsWebsite.Interfaces;

public interface ITaxonomyService
{
    Task<IEnumerable<Brand>> GetFullTaxonomyAsync();
    Task<IEnumerable<Brand>> GetBrandsAsync();
    Task<IEnumerable<Brand>> GetBrandsByCategoryAsync(int categoryId);
    Task<IEnumerable<Model>> GetModelsByBrandAsync(int brandId);
    Task<IEnumerable<Generation>> GetGenerationsByModelAsync(int modelId);
    Task<IEnumerable<EngineVersion>> GetEnginesByGenerationAsync(int generationId);
    Task<IEnumerable<FuelType>> GetFuelTypesAsync();
    Task<IEnumerable<Gearbox>> GetGearboxesAsync();
    Task<IEnumerable<BodyType>> GetBodyTypesAsync();
    Task<IEnumerable<DriveType>> GetDriveTypesAsync();
    Task<IEnumerable<CarColor>> GetColorsAsync();
    Task<IEnumerable<Feature>> GetFeaturesAsync();
    Task<IEnumerable<VehicleCategory>> GetVehicleCategoriesAsync();
    Task<IEnumerable<FeatureCategory>> GetFeatureCategoriesAsync();
}
