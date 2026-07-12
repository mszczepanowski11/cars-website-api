using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using cars_website_api.CarsWebsite.Interfaces;

namespace cars_website_api.CarsWebsite.Controllers
{
    // Behind [Authorize] (not public) since it proxies a rate-limited external API
    // (20 req/s, 100/min per CEPiK's own docs) - only logged-in users adding an advert can trigger it.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CepikController : ControllerBase
    {
        private readonly ICepikService _cepikService;

        public CepikController(ICepikService cepikService)
        {
            _cepikService = cepikService;
        }

        public class CepikLookupRequestDto
        {
            public string Wojewodztwo { get; set; } = string.Empty;
            public string Vin { get; set; } = string.Empty;
        }

        [HttpPost("lookup")]
        public async Task<IActionResult> Lookup([FromBody] CepikLookupRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Wojewodztwo) || string.IsNullOrWhiteSpace(dto.Vin))
                return BadRequest(new { errorCode = "invalid_request", errorMessage = "Podaj województwo i numer VIN." });

            var vin = dto.Vin.Trim().ToUpperInvariant();
            if (vin.Length != 17)
                return BadRequest(new { errorCode = "invalid_vin", errorMessage = "Numer VIN musi mieć 17 znaków." });

            var result = await _cepikService.LookupByVinAsync(dto.Wojewodztwo.Trim(), vin);
            if (!result.Success)
                return Ok(new { success = false, errorCode = result.ErrorCode, errorMessage = result.ErrorMessage });

            return Ok(new { success = true, vehicle = result.Vehicle });
        }
    }
}
