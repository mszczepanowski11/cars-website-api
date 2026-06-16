using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using cars_website_api.CarsWebsite.DTOs.Advert;
using cars_website_api.CarsWebsite.DTOs.Car;
using cars_website_api.CarsWebsite.Interfaces;

namespace cars_website_api.CarsWebsite.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TaxonomyController : ControllerBase
    {
        private readonly ITaxonomyService _taxonomyService;
        private readonly IMapper _mapper;

        public TaxonomyController(ITaxonomyService taxonomyService, IMapper mapper)
        {
            _taxonomyService = taxonomyService;
            _mapper = mapper;
        }

        [HttpGet("full")]
        public async Task<IActionResult> GetFullTaxonomy()
            => Ok(await _taxonomyService.GetFullTaxonomyAsync());

        [HttpGet("brands")]
        public async Task<IActionResult> GetBrands()
            => Ok(await _taxonomyService.GetBrandsAsync());

        [HttpGet("brands/category/{categoryId}")]
        public async Task<IActionResult> GetBrandsByCategory(int categoryId)
            => Ok(await _taxonomyService.GetBrandsByCategoryAsync(categoryId));

        [HttpGet("brands/{brandId}/models")]
        public async Task<IActionResult> GetModelsByBrand(int brandId)
            => Ok(await _taxonomyService.GetModelsByBrandAsync(brandId));

        [HttpGet("models/{modelId}/generations")]
        public async Task<IActionResult> GetGenerationsByModel(int modelId)
            => Ok(await _taxonomyService.GetGenerationsByModelAsync(modelId));

        [HttpGet("generations/{generationId}/engines")]
        public async Task<IActionResult> GetEnginesByGeneration(int generationId)
        {
            var engines = await _taxonomyService.GetEnginesByGenerationAsync(generationId);
            return Ok(_mapper.Map<IEnumerable<EngineVersionDto>>(engines));
        }

        [HttpGet("fueltypes")]
        public async Task<IActionResult> GetFuelTypes()
            => Ok(await _taxonomyService.GetFuelTypesAsync());

        [HttpGet("gearboxes")]
        public async Task<IActionResult> GetGearboxes()
            => Ok(await _taxonomyService.GetGearboxesAsync());

        [HttpGet("bodytypes")]
        public async Task<IActionResult> GetBodyTypes()
            => Ok(await _taxonomyService.GetBodyTypesAsync());

        [HttpGet("drive-types")]
        public async Task<IActionResult> GetDriveTypes()
            => Ok(await _taxonomyService.GetDriveTypesAsync());

        [HttpGet("colors")]
        public async Task<IActionResult> GetColors()
            => Ok(await _taxonomyService.GetColorsAsync());

        [HttpGet("features")]
        public async Task<IActionResult> GetFeatures()
            => Ok(await _taxonomyService.GetFeaturesAsync());

        [HttpGet("categories")]
        public async Task<IActionResult> GetVehicleCategories()
            => Ok(await _taxonomyService.GetVehicleCategoriesAsync());

        [HttpGet("feature-categories")]
        public async Task<IActionResult> GetFeatureCategories()
            => Ok(await _taxonomyService.GetFeatureCategoriesAsync());

        [HttpGet("feature-categories/by-vehicle/{vehicleCategoryId}")]
        public async Task<IActionResult> GetFeatureCategoriesByVehicle(int vehicleCategoryId)
        {
            var categories = await _taxonomyService.GetFeatureCategoriesByVehicleCategoryAsync(vehicleCategoryId);
            return Ok(_mapper.Map<IEnumerable<FeatureCategoryDto>>(categories));
        }
    }
}
