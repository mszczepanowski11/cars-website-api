using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CarsWebsite;

// Global identifier + text helpers for the Business Directory (blueprint sections 04-05).
// Kept dependency-free (no external ULID lib) so it works everywhere the domain is referenced.
public static class CarizoId
{
    // crz:<type>:<country>:<token>  e.g. "crz:org:pl:9f3a1c8b7d2e4056"
    // token = 16 hex chars from a crypto RNG - collision-safe at directory scale, and independent
    // of the DB auto-increment id so it survives re-imports/migrations.
    public static string New(string type, string? countryCode)
    {
        var region = string.IsNullOrWhiteSpace(countryCode) ? "xx" : countryCode.Trim().ToLowerInvariant();
        Span<byte> buf = stackalloc byte[8];
        RandomNumberGenerator.Fill(buf);
        var token = Convert.ToHexString(buf).ToLowerInvariant();
        return $"crz:{type}:{region}:{token}";
    }

    // Accent-stripped, lowercased, whitespace-collapsed - for idempotent seeding and dedup keys.
    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var lowered = s.Trim().ToLowerInvariant();
        var decomposed = lowered.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch);
        }
        var collapsed = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ");
        return collapsed.Trim();
    }

    // URL-safe slug from a name (+ optional discriminator on collision, e.g. city).
    public static string Slugify(string? s)
    {
        var norm = Normalize(s);
        var slug = System.Text.RegularExpressions.Regex.Replace(norm, @"[^a-z0-9]+", "-").Trim('-');
        return slug.Length == 0 ? "firma" : slug;
    }
}
