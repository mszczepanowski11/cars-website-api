using CarsWebsite;
using cars_website_api.CarsWebsite.DTOs.Directory;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace cars_website_api.CarsWebsite.Services;

// Auto-translation engine for the directory (blueprint: automatyczne tłumaczenia). Hangfire
// recurring job: finds active companies missing translations for the target languages and fills
// their I18n JSON via ITranslationProvider. No-op when no provider is configured (ships disabled).
//
// Target languages default to the main EU markets; override with TRANSLATION_TARGET_LANGS
// (comma-separated ISO 639-1, e.g. "en,de,fr,uk").
public class DirectoryTranslationJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DirectoryTranslationJob> _logger;

    public DirectoryTranslationJob(IServiceScopeFactory scopeFactory, ILogger<DirectoryTranslationJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    private static string[] TargetLangs()
    {
        var env = Environment.GetEnvironmentVariable("TRANSLATION_TARGET_LANGS");
        if (!string.IsNullOrWhiteSpace(env))
            return env.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                      .Select(s => s.ToLowerInvariant()).ToArray();
        return new[] { "en", "de", "fr" };
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ITranslationProvider>();
        if (!provider.IsConfigured)
        {
            _logger.LogInformation("[DirectoryTranslationJob] skipped - no translation provider configured (set TRANSLATION_API_KEY).");
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var targets = TargetLangs();

        // Batch-bounded per run so one pass never hammers the provider or the DB; the recurring
        // schedule works through the backlog over time.
        var batch = await db.DirectoryCompanies
            .Where(d => d.Status == "active")
            .OrderBy(d => d.UpdatedAt)
            .Take(50)
            .ToListAsync(ct);

        int translated = 0;
        foreach (var company in batch)
        {
            if (ct.IsCancellationRequested) break;
            var existing = ParseI18n(company.I18n);
            var baseLang = string.IsNullOrWhiteSpace(company.Language) ? "pl" : company.Language.ToLowerInvariant();
            bool changed = false;

            foreach (var lang in targets)
            {
                if (lang == baseLang) continue;
                if (existing.ContainsKey(lang)) continue; // already has this language

                var name = await provider.TranslateAsync(company.Name, lang, baseLang, ct);
                string? desc = null;
                if (!string.IsNullOrWhiteSpace(company.Description))
                    desc = await provider.TranslateAsync(company.Description!, lang, baseLang, ct);

                if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(desc))
                {
                    existing[lang] = new LocalizedTextDto { Name = name, Description = desc };
                    changed = true;
                }
            }

            if (changed)
            {
                company.I18n = existing.Count > 0 ? JsonSerializer.Serialize(existing) : null;
                translated++;
            }
        }

        if (translated > 0) await db.SaveChangesAsync(ct);
        _logger.LogInformation("[DirectoryTranslationJob] done: {Count} companies translated into [{Langs}]",
            translated, string.Join(",", targets));
    }

    private static Dictionary<string, LocalizedTextDto> ParseI18n(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, LocalizedTextDto>>(json) ?? new();
        }
        catch { return new(); }
    }
}
