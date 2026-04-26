using Microsoft.AspNetCore.Mvc;
using cars_website_api.CarsWebsite.Interfaces;

namespace cars_website_api.CarsWebsite.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TaxonomyController : ControllerBase
    {
        private readonly ITaxonomyService _taxonomyService;

        public TaxonomyController(ITaxonomyService taxonomyService)
        {
            _taxonomyService = taxonomyService;
        }
        
        
        [HttpGet("full")]
        public async Task<IActionResult> GetFullTaxonomy()
        {
            var result = await _taxonomyService.GetFullTaxonomyAsync();
            return Ok(result);
        }

        [HttpGet("brands")]
        public async Task<IActionResult> GetBrands()
        {
            var result = await _taxonomyService.GetBrandsAsync();
            return Ok(result);
        }

        [HttpGet("brands/{brandId}/models")]
        public async Task<IActionResult> GetModelsByBrand(int brandId)
        {
            var result = await _taxonomyService.GetModelsByBrandAsync(brandId);
            return Ok(result);
        }

        [HttpGet("models/{modelId}/generations")]
        public async Task<IActionResult> GetGenerationsByModel(int modelId)
        {
            var result = await _taxonomyService.GetGenerationsByModelAsync(modelId);
            return Ok(result);
        }

        [HttpGet("generations/{generationId}/engines")]
        public async Task<IActionResult> GetEnginesByGeneration(int generationId)
        {
            var result = await _taxonomyService.GetEnginesByGenerationAsync(generationId);
            return Ok(result);
        }

        [HttpGet("fueltypes")]
        public async Task<IActionResult> GetFuelTypes()
        {
            var result = await _taxonomyService.GetFuelTypesAsync();
            return Ok(result);
        }

        [HttpGet("gearboxes")]
        public async Task<IActionResult> GetGearboxes()
        {
            var result = await _taxonomyService.GetGearboxesAsync();
            return Ok(result);
        }

        [HttpGet("bodytypes")]
        public async Task<IActionResult> GetBodyTypes()
        {
            var result = await _taxonomyService.GetBodyTypesAsync();
            return Ok(result);
        }

        [HttpGet("features")]
        public async Task<IActionResult> GetFeatures()
        {
            var result = await _taxonomyService.GetFeaturesAsync();
            return Ok(result);
        }
    }
}
