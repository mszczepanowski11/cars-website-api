using CarsWebsite;
using cars_website_api.CarsWebsite.Interfaces;

namespace cars_website_api.CarsWebsite.Services;

public class PartnerFeedFetchService : IPartnerFeedFetchService
{
    private const long MaxContentBytes = 15 * 1024 * 1024; // 15 MB

    private readonly HttpClient _http;
    private readonly ILogger<PartnerFeedFetchService> _logger;

    public PartnerFeedFetchService(HttpClient http, ILogger<PartnerFeedFetchService> logger)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(15);
        _logger = logger;
    }

    public async Task<PartnerFeedFetchResult> FetchAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Fail("Nieprawidłowy adres URL - wymagany http:// lub https://.");
        }

        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);

            if ((int)response.StatusCode is >= 300 and < 400)
                return Fail("Adres zwrócił przekierowanie - podaj bezpośredni link do pliku.");
            if (!response.IsSuccessStatusCode)
                return Fail($"Serwer zwrócił błąd {(int)response.StatusCode}.");

            var declaredLength = response.Content.Headers.ContentLength;
            if (declaredLength.HasValue && declaredLength.Value > MaxContentBytes)
                return Fail("Plik jest zbyt duży (limit 15 MB).");

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(chunk)) > 0)
            {
                total += read;
                if (total > MaxContentBytes)
                    return Fail("Plik jest zbyt duży (limit 15 MB).");
                buffer.Write(chunk, 0, read);
            }

            var content = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
            var trimmed = content.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (trimmed.Length == 0)
                return Fail("Plik pod podanym adresem jest pusty.");

            var format = trimmed.StartsWith('<') ? PartnerFeedFormat.Xml : PartnerFeedFormat.Csv;
            return new PartnerFeedFetchResult { Success = true, Content = content, Format = format };
        }
        catch (TaskCanceledException)
        {
            return Fail("Przekroczono limit czasu połączenia (15 s).");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "[PartnerFeedFetch] Failed to fetch {Url}", url);
            return Fail("Nie udało się pobrać pliku spod podanego adresu.");
        }
    }

    private static PartnerFeedFetchResult Fail(string error) => new() { Success = false, Error = error };
}
