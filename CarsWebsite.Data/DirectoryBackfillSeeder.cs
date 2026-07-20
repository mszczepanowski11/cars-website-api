using cars_website_api.CarsWebsite.Domain.Entities;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace cars_website_api.CarsWebsite.Data;

// Backfills the Business Directory from companies that ALREADY exist in the system and gave us
// their data with consent - business users and approved partners. This is the honest way to seed
// the first rows: no fabricated/scraped contact data, only companies that registered themselves.
// External sources (rejestry, OSM, feeds) are a later pipeline (blueprint section 10).
//
// Idempotent by NameNormalized (+ city): re-running never duplicates a company. New fields from a
// registered account are filled in on re-run if the directory row was missing them.
public static class DirectoryBackfillSeeder
{
    public static void Seed(AppDbContext db, ILogger logger)
    {
        try
        {
            // Index existing directory rows by their normalized name for O(1) idempotency checks.
            var existing = db.DirectoryCompanies.ToList()
                .GroupBy(d => d.NameNormalized)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            int added = 0, updated = 0;

            // ── 1. Business users (AccountType.Business with a company name) ──────────────
            var businessUsers = db.Users.AsNoTracking()
                .Where(u => u.AccountType == AccountType.Business && u.CompanyName != null && u.CompanyName != "")
                .ToList();

            foreach (var u in businessUsers)
            {
                var norm = CarizoId.Normalize(u.CompanyName);
                if (norm.Length == 0) continue;
                var category = MapBusinessType(u.BusinessType);

                if (existing.TryGetValue(norm, out var row))
                {
                    // Fill gaps only - never overwrite a curated value with an emptier one.
                    if (string.IsNullOrEmpty(row.City) && !string.IsNullOrEmpty(u.City)) { row.City = u.City; updated++; }
                    if (string.IsNullOrEmpty(row.Phone) && !string.IsNullOrEmpty(u.PhoneNumber)) { row.Phone = u.PhoneNumber; }
                    continue;
                }

                var company = new DirectoryCompany
                {
                    PublicId = CarizoId.New("org", "PL"),
                    Name = u.CompanyName!.Trim(),
                    NameNormalized = norm,
                    Category = category,
                    CountryCode = "PL",
                    City = u.City,
                    Phone = u.PhoneNumber,
                    Email = u.Email,
                    EmailType = ClassifyEmail(u.Email),
                    Language = "pl",
                    Status = "active",           // a registered account is a confirmed company
                    Source = "backfill:user",
                    Slug = UniqueSlug(db, existing, u.CompanyName, u.City),
                };
                db.DirectoryCompanies.Add(company);
                existing[norm] = company;
                added++;
            }

            // ── 2. Approved partners (companies that pushed feeds) ────────────────────────
            var partners = db.Partners.AsNoTracking().Where(p => p.IsActive).ToList();
            foreach (var p in partners)
            {
                var norm = CarizoId.Normalize(p.CompanyName);
                if (norm.Length == 0 || existing.ContainsKey(norm)) continue;

                var company = new DirectoryCompany
                {
                    PublicId = CarizoId.New("org", "PL"),
                    Name = p.CompanyName.Trim(),
                    NameNormalized = norm,
                    Category = "dealerzy",       // a feed-pushing partner is a dealer/importer
                    CountryCode = "PL",
                    Email = p.ContactEmail,
                    EmailType = ClassifyEmail(p.ContactEmail),
                    Website = p.FeedUrl != null ? TryHost(p.FeedUrl) : null,
                    Language = "pl",
                    Status = "active",
                    Source = "backfill:partner",
                    PartnerId = p.Id,
                    Slug = UniqueSlug(db, existing, p.CompanyName, null),
                };
                db.DirectoryCompanies.Add(company);
                existing[norm] = company;
                added++;
            }

            if (added > 0 || updated > 0) db.SaveChanges();
            logger.LogWarning("[DIRECTORY-BACKFILL] DirectoryBackfillSeeder done: added={Added} updated={Updated} totalRows={Total}",
                added, updated, existing.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[DIRECTORY-BACKFILL] failed: {Msg}", ex.Message);
        }
    }

    private static string MapBusinessType(BusinessType? t) => t switch
    {
        BusinessType.Dealer => "dealerzy",
        BusinessType.Komis  => "komisy",
        _                    => "firmy",
    };

    private static string? ClassifyEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) return null;
        var local = email.Split('@')[0].ToLowerInvariant();
        string[] role = { "biuro", "office", "kontakt", "contact", "info", "sprzedaz", "sales", "sekretariat", "handel" };
        return role.Any(r => local.Contains(r)) ? "role" : "personal";
    }

    private static string? TryHost(string url)
    {
        try { return new Uri(url.Contains("://") ? url : "https://" + url).Host; }
        catch { return null; }
    }

    private static string UniqueSlug(AppDbContext db, Dictionary<string, DirectoryCompany> existing, string name, string? city)
    {
        var baseSlug = CarizoId.Slugify(name);
        bool Taken(string s) => existing.Values.Any(e => e.Slug == s) || db.DirectoryCompanies.Any(d => d.Slug == s);
        if (!Taken(baseSlug)) return baseSlug;
        var withCity = $"{baseSlug}-{CarizoId.Slugify(city)}";
        if (!string.IsNullOrWhiteSpace(city) && !Taken(withCity)) return withCity;
        return $"{baseSlug}-{Guid.NewGuid().ToString("N")[..6]}";
    }
}
