using CarsWebsite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace cars_website_api.CarsWebsite.Data;

// Faza 8 of the category/attribute restructure: one-time backfill converting the deprecated
// CarAdvert.YoutubeUrl/PdfBrochureUrl columns into real AdvertDocument rows. Idempotent by
// AdvertId - an advert that already has any AdvertDocument row is skipped entirely, so this is
// safe to run on every startup (both because a re-run shouldn't duplicate rows, and because a
// seller filling in YoutubeUrl/PdfBrochureUrl again post-migration would otherwise never get
// backfilled since Faza 9's column removal hasn't happened yet).
public static class AdvertDocumentBackfillSeeder
{
    public static void Seed(AppDbContext db, ILogger logger)
    {
        var advertIdsWithDocuments = db.AdvertDocuments.Select(d => d.AdvertId).ToHashSet();

        var candidates = db.CarAdverts
            .Where(a => (a.YoutubeUrl != null && a.YoutubeUrl != "") || (a.PdfBrochureUrl != null && a.PdfBrochureUrl != ""))
            .Select(a => new { a.Id, a.YoutubeUrl, a.PdfBrochureUrl })
            .ToList()
            .Where(a => !advertIdsWithDocuments.Contains(a.Id))
            .ToList();

        if (candidates.Count == 0) return;

        int added = 0;
        foreach (var a in candidates)
        {
            var order = 0;
            if (!string.IsNullOrWhiteSpace(a.YoutubeUrl))
            {
                db.AdvertDocuments.Add(new AdvertDocument { AdvertId = a.Id, Url = a.YoutubeUrl, Type = AdvertDocumentType.Video, SortOrder = order++ });
                added++;
            }
            if (!string.IsNullOrWhiteSpace(a.PdfBrochureUrl))
            {
                db.AdvertDocuments.Add(new AdvertDocument { AdvertId = a.Id, Url = a.PdfBrochureUrl, Type = AdvertDocumentType.Pdf, SortOrder = order++ });
                added++;
            }
        }

        db.SaveChanges();
        logger.LogWarning("[ADVERT-DOCS-BACKFILL] AdvertDocumentBackfillSeeder done: advertsBackfilled={Adverts} documentsAdded={Documents}", candidates.Count, added);
    }
}
