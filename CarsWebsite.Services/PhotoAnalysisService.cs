using System.Net.Http.Json;

namespace cars_website_api.CarsWebsite.Services;

public class PhotoAnalysisResult
{
    public int Score { get; set; } // 1-10
    public List<string> Issues { get; set; } = new();
    public List<string> Suggestions { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}

public interface IPhotoAnalysisService
{
    Task<PhotoAnalysisResult> AnalyzeAsync(string imageUrl);
}

public class PhotoAnalysisService : IPhotoAnalysisService
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly ILogger<PhotoAnalysisService> _logger;

    public PhotoAnalysisService(HttpClient httpClient, IConfiguration config, ILogger<PhotoAnalysisService> logger)
    {
        _httpClient = httpClient;
        _apiKey = config["ANTHROPIC_API_KEY"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        _logger = logger;
    }

    public async Task<PhotoAnalysisResult> AnalyzeAsync(string imageUrl)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            return new PhotoAnalysisResult
            {
                Score = 0,
                Issues = new List<string> { "Analiza AI niedostępna - brak klucza API" },
                Suggestions = new List<string> { "Skonfiguruj ANTHROPIC_API_KEY w zmiennych środowiskowych" },
                Summary = "Usługa analizy AI nie jest skonfigurowana."
            };
        }

        try
        {
            var requestBody = new
            {
                model = "claude-haiku-4-5-20251001",
                max_tokens = 1024,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "image", source = new { type = "url", url = imageUrl } },
                            new { type = "text", text = @"Jesteś ekspertem od fotografii motoryzacyjnej. Oceń jakość tego zdjęcia samochodu w skali 1-10.

Odpowiedz TYLKO w formacie JSON (bez markdown, bez ```):
{
  ""score"": <liczba 1-10>,
  ""issues"": [<lista problemów po polsku, max 3>],
  ""suggestions"": [<lista sugestii poprawy po polsku, max 3>],
  ""summary"": ""<krótkie podsumowanie po polsku, 1 zdanie>""
}

Oceniaj: ostrość, oświetlenie, kadr, tło, widoczność pojazdu, profesjonalizm." }
                        }
                    }
                }
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var response = await _httpClient.PostAsJsonAsync("https://api.anthropic.com/v1/messages", requestBody);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Anthropic API error: {Status} {Body}", response.StatusCode, responseBody);
                return FallbackResult();
            }

            // Parse response
            using var doc = System.Text.Json.JsonDocument.Parse(responseBody);
            var content = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";

            // Parse the JSON content returned by Claude
            using var resultDoc = System.Text.Json.JsonDocument.Parse(content);
            var root = resultDoc.RootElement;

            var result = new PhotoAnalysisResult
            {
                Score = root.GetProperty("score").GetInt32(),
                Summary = root.GetProperty("summary").GetString() ?? ""
            };

            if (root.TryGetProperty("issues", out var issuesEl))
                foreach (var issue in issuesEl.EnumerateArray())
                    result.Issues.Add(issue.GetString() ?? "");

            if (root.TryGetProperty("suggestions", out var suggestionsEl))
                foreach (var suggestion in suggestionsEl.EnumerateArray())
                    result.Suggestions.Add(suggestion.GetString() ?? "");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Photo analysis failed for {Url}", imageUrl);
            return FallbackResult();
        }
    }

    private static PhotoAnalysisResult FallbackResult() => new()
    {
        Score = 5,
        Issues = new List<string>(),
        Suggestions = new List<string> { "Upewnij się, że pojazd jest dobrze oświetlony", "Sfotografuj pojazd na czystym tle", "Zrób zdjęcia z wielu stron" },
        Summary = "Nie udało się przeanalizować zdjęcia automatycznie."
    };
}
