using cars_website_api.CarsWebsite.Interfaces;
using System.Text.Json;

namespace cars_website_api.CarsWebsite.Services;

// DeepL-compatible HTTP translation provider. Activated by env vars:
//   TRANSLATION_API_KEY   - the provider API key (absence = disabled, IsConfigured=false)
//   TRANSLATION_API_URL   - endpoint (default DeepL: https://api-free.deepl.com/v2/translate)
// The request/response shape follows DeepL's; swapping to another provider means swapping this
// one class. Nothing else in the system knows which engine is behind ITranslationProvider.
public class HttpTranslationProvider : ITranslationProvider
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<HttpTranslationProvider> _logger;
    private readonly string? _apiKey;
    private readonly string _apiUrl;

    public HttpTranslationProvider(IHttpClientFactory httpFactory, ILogger<HttpTranslationProvider> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _apiKey = Environment.GetEnvironmentVariable("TRANSLATION_API_KEY");
        _apiUrl = Environment.GetEnvironmentVariable("TRANSLATION_API_URL")
                  ?? "https://api-free.deepl.com/v2/translate";
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<string?> TranslateAsync(string text, string targetLang, string? sourceLang, CancellationToken ct)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            var form = new List<KeyValuePair<string, string>>
            {
                new("auth_key", _apiKey!),
                new("text", text),
                new("target_lang", targetLang.ToUpperInvariant()),
            };
            if (!string.IsNullOrWhiteSpace(sourceLang))
                form.Add(new("source_lang", sourceLang.ToUpperInvariant()));

            using var resp = await client.PostAsync(_apiUrl, new FormUrlEncodedContent(form), ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Translation] provider returned {Status} for target {Lang}", resp.StatusCode, targetLang);
                return null;
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("translations", out var arr) && arr.GetArrayLength() > 0
                && arr[0].TryGetProperty("text", out var t))
                return t.GetString();
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Translation] failed for target {Lang} (non-fatal)", targetLang);
            return null;
        }
    }
}
