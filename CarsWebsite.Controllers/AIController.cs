using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.DTOs.Advert;
using cars_website_api.CarsWebsite.DTOs.Admin;
using CarsWebsite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
public class AIController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;

    public AIController(AppDbContext db, IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _config = config;
    }

    // POST /api/ai/validate — validate brand/engine combination
    [HttpPost("validate")]
    [Authorize]
    public async Task<IActionResult> ValidateCombination([FromBody] ValidateCombinationDto dto)
    {
        var warnings = new List<string>();

        string? brandName = dto.BrandName;
        if (brandName == null && dto.BrandId.HasValue)
        {
            var brand = await _db.Brands.FindAsync(dto.BrandId.Value);
            brandName = brand?.Name;
        }

        // Brands that never made diesel engines
        var noDieselBrands = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Ferrari", "Lamborghini", "Pagani", "Koenigsegg", "Bugatti", "McLaren",
            "Aston Martin", "Lotus", "Morgan"
        };

        if (brandName != null && dto.FuelTypeName != null)
        {
            var fuelLower = dto.FuelTypeName.ToLowerInvariant();
            if (noDieselBrands.Contains(brandName) && fuelLower.Contains("diesel"))
                warnings.Add($"{brandName} nigdy nie produkowało silników Diesla. Sprawdź poprawność danych.");

            // Electric-only brands shouldn't have combustion engines
            var evOnlyBrands = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Tesla", "Rivian", "Lucid" };
            if (evOnlyBrands.Contains(brandName) && !fuelLower.Contains("elektr"))
                warnings.Add($"{brandName} produkuje wyłącznie pojazdy elektryczne. Sprawdź rodzaj paliwa.");
        }

        // Cross-brand engine name checks
        if (dto.EngineName != null && brandName != null)
        {
            var e = dto.EngineName.ToUpperInvariant();
            // BMW uses 'd'/'xd', not 'TDI'
            if (brandName.Equals("BMW", StringComparison.OrdinalIgnoreCase) && e.Contains("TDI"))
                warnings.Add("BMW stosuje oznaczenie 'd' lub 'xd' dla silników diesla, nie 'TDI' (to oznaczenie VAG/Audi).");
            // Mercedes uses 'CDI', not 'TDI'
            if (brandName.StartsWith("Mercedes", StringComparison.OrdinalIgnoreCase) && e.Contains("TDI"))
                warnings.Add("Mercedes-Benz stosuje oznaczenie 'CDI' dla silników diesla, nie 'TDI'.");
            // VAG brands shouldn't use CDI
            var vagBrands = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Audi", "Volkswagen", "Skoda", "Seat", "Cupra", "Porsche" };
            if (vagBrands.Contains(brandName) && e.Contains("CDI"))
                warnings.Add($"{brandName} nie używa oznaczenia 'CDI'. To zastrzeżone oznaczenie Mercedes-Benz.");
            // Fiat/Alfa using BMW-style
            if ((brandName.Equals("Fiat", StringComparison.OrdinalIgnoreCase) || brandName.Equals("Alfa Romeo", StringComparison.OrdinalIgnoreCase)) && e.Contains("TFSI"))
                warnings.Add($"{brandName} nie używa oznaczenia 'TFSI'. To oznaczenie silników Audi.");
        }

        // Physics sanity check: power per liter
        if (dto.PowerHP.HasValue && dto.Displacement.HasValue && dto.Displacement.Value > 0)
        {
            var ppl = (double)dto.PowerHP.Value / (dto.Displacement.Value / 1000.0);
            if (ppl > 450)
                warnings.Add($"Moc {dto.PowerHP} KM przy pojemności {dto.Displacement} cm³ daje {ppl:F0} KM/litr — wartość fizycznie niemożliwa. Sprawdź dane.");
            else if (ppl > 250 && brandName != null && !noDieselBrands.Contains(brandName))
                warnings.Add($"Moc {dto.PowerHP} KM przy pojemności {dto.Displacement} cm³ ({ppl:F0} KM/litr) jest bardzo wysoka. Sprawdź poprawność danych.");
        }

        return Ok(new { warnings, valid = warnings.Count == 0 });
    }

    // POST /api/ai/describe — generate advert description
    [HttpPost("describe")]
    [Authorize]
    [EnableRateLimiting("ai")]
    public async Task<IActionResult> GenerateDescription([FromBody] AiDescriptionRequestDto dto)
    {
        var apiKey = _config["Anthropic:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

        if (!string.IsNullOrEmpty(apiKey))
        {
            try
            {
                var prompt = BuildDescriptionPrompt(dto);
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("x-api-key", apiKey);
                client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

                var body = new {
                    model = "claude-haiku-4-5-20251001",
                    max_tokens = 600,
                    messages = new[] { new { role = "user", content = prompt } }
                };

                using var resp = await client.PostAsJsonAsync("https://api.anthropic.com/v1/messages", body);
                if (resp.IsSuccessStatusCode)
                {
                    var result = await resp.Content.ReadFromJsonAsync<AnthropicResponse>();
                    var text = result?.Content?.FirstOrDefault()?.Text;
                    if (!string.IsNullOrWhiteSpace(text))
                        return Ok(new { description = text, source = "ai" });
                }
            }
            catch { /* fall through to template */ }
        }

        return Ok(new { description = GenerateTemplateDescription(dto), source = "template" });
    }

    // POST /api/ai/analyze — analyze advert quality
    [HttpPost("analyze")]
    [Authorize]
    public IActionResult AnalyzeQuality([FromBody] AnalyzeQualityDto dto)
    {
        var issues = new List<string>();
        var suggestions = new List<string>();
        int score = 100;

        if (string.IsNullOrWhiteSpace(dto.Title)) { issues.Add("Brak tytułu ogłoszenia."); score -= 15; }
        else if (dto.Title.Length < 15) { suggestions.Add("Tytuł jest zbyt krótki — opisz markę, model i rok."); score -= 5; }

        if (string.IsNullOrWhiteSpace(dto.Description)) { issues.Add("Brak opisu pojazdu."); score -= 20; }
        else if (dto.Description.Length < 100) { suggestions.Add("Opis jest zbyt krótki (mniej niż 100 znaków)."); score -= 10; }
        else if (dto.Description.Length < 300) { suggestions.Add("Rozbuduj opis — im więcej szczegółów, tym więcej zapytań."); score -= 5; }

        if (dto.PhotoCount == 0) { issues.Add("Brak zdjęć — ogłoszenia bez zdjęć nie są wyświetlane."); score -= 25; }
        else if (dto.PhotoCount < 3) { suggestions.Add($"Dodaj więcej zdjęć ({dto.PhotoCount}/3 minimum)."); score -= 15; }
        else if (dto.PhotoCount < 10) { suggestions.Add($"Dodaj więcej zdjęć ({dto.PhotoCount}/10). Ogłoszenia z 10+ zdjęciami sprzedają się 3× szybciej."); score -= 5; }

        if (!dto.HasBrand) { issues.Add("Nie wybrano marki — konieczne do wyszukiwania."); score -= 10; }
        if (!dto.HasModel) { suggestions.Add("Nie wybrano modelu — uzupełnij."); score -= 5; }
        if (!dto.HasYear) { issues.Add("Nie podano roku produkcji."); score -= 8; }
        if (!dto.HasPrice) { issues.Add("Nie podano ceny."); score -= 10; }
        if (!dto.HasFuelType) { suggestions.Add("Nie wybrano rodzaju paliwa."); score -= 3; }
        if (dto.FeatureCount < 3) { suggestions.Add($"Zaznacz przynajmniej 3 elementy wyposażenia (masz {dto.FeatureCount})."); score -= 5; }
        if (!dto.HasVin) { suggestions.Add("Podanie numeru VIN buduje zaufanie kupującego."); score -= 2; }

        var finalScore = Math.Max(score, 0);
        return Ok(new {
            score = finalScore,
            grade = finalScore >= 90 ? "excellent" : finalScore >= 75 ? "good" : finalScore >= 50 ? "ok" : "poor",
            issues,
            suggestions
        });
    }

    private static string GenerateTemplateDescription(AiDescriptionRequestDto dto)
    {
        var parts = new List<string>();
        var carLabel = new[] { dto.Brand, dto.Model, dto.Generation, dto.Year?.ToString() }
            .Where(s => !string.IsNullOrEmpty(s));
        var intro = string.Join(" ", carLabel);
        if (!string.IsNullOrEmpty(intro)) parts.Add($"Sprzedam {intro}.");

        var tech = new List<string>();
        if (!string.IsNullOrEmpty(dto.FuelType)) tech.Add($"silnik {dto.FuelType.ToLowerInvariant()}");
        if (dto.EngineCapacity.HasValue) tech.Add($"{dto.EngineCapacity} cm³");
        if (dto.PowerHP.HasValue) tech.Add($"{dto.PowerHP} KM");
        if (!string.IsNullOrEmpty(dto.Gearbox))
            tech.Add(dto.Gearbox.ToLowerInvariant().Contains("auto") ? "skrzynia automatyczna" : "skrzynia manualna");
        if (tech.Any()) parts.Add($"Pojazd wyposażony w {string.Join(", ", tech)}.");

        if (dto.Mileage.HasValue) parts.Add($"Przebieg: {dto.Mileage:N0} km.");
        if (dto.HasFullServiceHistory) parts.Add("Pełna historia serwisowa udokumentowana — serwisowany wyłącznie w ASO.");
        else if (dto.HasServiceBook) parts.Add("Posiada książkę serwisową.");
        if (dto.OwnersCount.HasValue)
            parts.Add(dto.OwnersCount == 1 ? "Jeden właściciel w Polsce." : $"{dto.OwnersCount} właścicieli.");
        if (dto.FeaturesCount > 0) parts.Add($"Bogato wyposażony — {dto.FeaturesCount} pozycji dodatkowego wyposażenia.");
        parts.Add("Stan techniczny bardzo dobry. Możliwość jazdy próbnej. Zapraszam do kontaktu.");

        return string.Join("\n\n", parts);
    }

    private static string BuildDescriptionPrompt(AiDescriptionRequestDto dto)
    {
        var carLabel = string.Join(" ", new[] { dto.Brand, dto.Model, dto.Generation, dto.Year?.ToString() }.Where(s => !string.IsNullOrEmpty(s)));
        return $"""
            Napisz profesjonalne ogłoszenie sprzedaży pojazdu po polsku. Użyj naturalnego, przekonującego języka.

            Dane pojazdu:
            - Pojazd: {carLabel}
            - Silnik: {dto.FuelType}, {dto.EngineCapacity} cm³, {dto.PowerHP} KM
            - Skrzynia biegów: {dto.Gearbox}
            - Przebieg: {dto.Mileage?.ToString("N0") ?? "nieznany"} km
            - Rok produkcji: {dto.Year}
            - Stan: {dto.Condition}
            - Historia serwisowa: {(dto.HasFullServiceHistory ? "pełna, dokumentowana" : dto.HasServiceBook ? "książka serwisowa" : "brak danych")}
            - Liczba właścicieli: {dto.OwnersCount?.ToString() ?? "nieznana"}
            - Dodatkowe wyposażenie: {dto.FeaturesCount} pozycji

            Napisz opis w 3-4 akapitach. Bez nagłówków, bez punktorów. Maksimum 350 słów.
            """;
    }
}
