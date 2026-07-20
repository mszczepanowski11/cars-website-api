namespace cars_website_api.CarsWebsite.Interfaces;

// Pluggable machine-translation backend for the directory (blueprint: automatyczne tłumaczenia).
// Kept behind an interface so the concrete engine (DeepL, Google, an LLM, ...) can change without
// touching the job that drives it. Ships disabled until an API key is configured.
public interface ITranslationProvider
{
    // True only when a provider is actually configured (e.g. an API key is present). When false,
    // the translation job and the admin "auto-translate" action no-op with a clear message
    // instead of pretending to work.
    bool IsConfigured { get; }

    // Translates one text into targetLang (ISO 639-1). sourceLang may be null (auto-detect).
    // Returns null on failure - the caller keeps the original rather than storing a bad value.
    Task<string?> TranslateAsync(string text, string targetLang, string? sourceLang, CancellationToken ct);
}
