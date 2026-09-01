using System.Text.Json;
using CarsWebsite;
using cars_website_api.CarsWebsite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Data;

// Taksonomia Etap 6: consolidates the overlapping categories the audit found.
//
// Unlike every other seeder in this codebase this one MOVES EXISTING LISTINGS, so it follows one
// hard rule throughout: re-point first, delete only once the source is provably empty. Every
// category removal is preceded by an explicit emptiness check covering adverts, subtypes, feature
// groups, attribute definitions and brand links - if any of them still resolves to the old
// category, it is left in place and the reason is logged rather than forced.
//
// Fully idempotent: once a merge has run, the source categories no longer exist and every step
// short-circuits.
public static class CategoryConsolidationSeeder
{
    private static string NKey(string? s) => (s ?? string.Empty).Trim().ToLowerInvariant();

    public static void Seed(AppDbContext db, ILogger logger)
    {
        logger.LogWarning("[STARTUP-TRACE] CategoryConsolidationSeeder entered");
        try { ConsolidateQuads(db, logger); }
        catch (Exception ex) { logger.LogError(ex, "[Consolidate] Quad consolidation failed"); }

        try { ConsolidateMachines(db, logger); }
        catch (Exception ex) { logger.LogError(ex, "[Consolidate] Machine consolidation failed"); }
    }

    // ── Quady ────────────────────────────────────────────────────────────────────────────────
    // A quad was representable in THREE places at once: as the "Quad" subtype of Motocykle, as the
    // standalone quady-atv category, and as a "Quad / ATV" option inside the motorcycleType
    // attribute. Listings therefore scattered across three shapes and no filter could find them
    // all. quady-atv wins (it is a top-level category with its own attributes); the other two are
    // removed after their listings are moved onto it.
    private static void ConsolidateQuads(AppDbContext db, ILogger logger)
    {
        var moto = db.VehicleCategories.FirstOrDefault(c => c.Slug == "motocykle");
        var quadCat = db.VehicleCategories.FirstOrDefault(c => c.Slug == "quady-atv");
        if (moto is null || quadCat is null) return;

        var quadSub = db.VehicleSubtypes.FirstOrDefault(v =>
            v.VehicleCategoryId == moto.Id && (v.Slug == "quad" || v.Name == "Quad"));

        if (quadSub is not null)
        {
            // Prefer a like-for-like subtype in the destination so the listing keeps its meaning;
            // fall back to no subtype rather than guessing wrongly.
            var destSub = db.VehicleSubtypes
                .Where(v => v.VehicleCategoryId == quadCat.Id)
                .AsEnumerable()
                .FirstOrDefault(v => NKey(v.Name).Contains("atv") || NKey(v.Name).Contains("quad"));

            var moved = db.CarAdverts.Where(a => a.VehicleSubtypeId == quadSub.Id).ToList();
            foreach (var a in moved)
            {
                a.VehicleCategoryId = quadCat.Id;
                a.VehicleSubtypeId = destSub?.Id;
            }
            if (moved.Count > 0) db.SaveChanges();

            var stillUsed = db.CarAdverts.Any(a => a.VehicleSubtypeId == quadSub.Id);
            if (stillUsed)
            {
                logger.LogWarning("[Consolidate] 'Quad' subtype still referenced after move - left in place");
            }
            else
            {
                db.VehicleSubtypes.Remove(quadSub);
                db.SaveChanges();
                logger.LogInformation("[Consolidate] Moved {Count} advert(s) from Motocykle/Quad to quady-atv and removed the subtype", moved.Count);
            }
        }

        // Drop the duplicate option from the motorcycle-type attribute so the form stops offering
        // a third way to say "quad". Existing listings that stored it keep their stored value -
        // the option list only drives what NEW listings can pick.
        var motoTypeDefs = db.AttributeDefinitions
            .Where(d => d.VehicleCategoryId == moto.Id && d.OptionsJson != null)
            .ToList();

        foreach (var def in motoTypeDefs)
        {
            string[]? options;
            try { options = JsonSerializer.Deserialize<string[]>(def.OptionsJson!); }
            catch { continue; }
            if (options is null) continue;

            var cleaned = options.Where(o => !NKey(o).Contains("quad")).ToArray();
            if (cleaned.Length == options.Length) continue;

            def.OptionsJson = JsonSerializer.Serialize(cleaned);
            db.SaveChanges();
            logger.LogInformation("[Consolidate] Removed the quad option from attribute '{Key}' on Motocykle", def.Key);
        }
    }

    // ── Maszyny ──────────────────────────────────────────────────────────────────────────────
    // `maszyny` ("Maszyny budowlane, rolnicze i przemysłowe") is a catch-all that overlaps both
    // budowlane and wozki-widlowe. Its actual subtypes are warehouse/industrial handling gear -
    // forklifts, reach trucks, stackers, cranes, hoists, conveyors, generators - NOT construction
    // machinery, so they are routed by what they really are rather than by the category's name:
    // anything forklift-shaped joins wozki-widlowe, the rest joins budowlane.
    private static void ConsolidateMachines(AppDbContext db, ILogger logger)
    {
        var src = db.VehicleCategories.FirstOrDefault(c => c.Slug == "maszyny");
        if (src is null) return; // already consolidated

        var forklift = db.VehicleCategories.FirstOrDefault(c => c.Slug == "wozki-widlowe");
        var construction = db.VehicleCategories.FirstOrDefault(c => c.Slug == "budowlane");
        if (construction is null)
        {
            logger.LogWarning("[Consolidate] Category 'budowlane' missing - machine consolidation skipped");
            return;
        }

        int TargetFor(string subtypeName)
        {
            var n = NKey(subtypeName);
            var isForkliftish = n.Contains("wózek") || n.Contains("wozek")
                             || n.Contains("reach truck") || n.Contains("układnica") || n.Contains("ukladnica");
            return isForkliftish && forklift is not null ? forklift.Id : construction.Id;
        }

        var subtypes = db.VehicleSubtypes.Where(v => v.VehicleCategoryId == src.Id).ToList();
        var movedAdverts = 0;

        foreach (var sub in subtypes)
        {
            var targetCatId = TargetFor(sub.Name);

            // If the destination already has a subtype meaning the same thing, merge into it
            // instead of creating a near-duplicate (and instead of violating the UNIQUE on
            // (VehicleCategoryId, Name) added in Etap 1).
            var twin = db.VehicleSubtypes
                .Where(v => v.VehicleCategoryId == targetCatId)
                .AsEnumerable()
                .FirstOrDefault(v => NKey(v.Name) == NKey(sub.Name));

            var adverts = db.CarAdverts.Where(a => a.VehicleSubtypeId == sub.Id).ToList();
            foreach (var a in adverts)
            {
                a.VehicleCategoryId = targetCatId;
                a.VehicleSubtypeId = twin?.Id ?? sub.Id;
                movedAdverts++;
            }

            if (twin is null) sub.VehicleCategoryId = targetCatId; // reparent, keeps advert links
            db.SaveChanges();

            if (twin is not null && !db.CarAdverts.Any(a => a.VehicleSubtypeId == sub.Id))
            {
                db.VehicleSubtypes.Remove(sub);
                db.SaveChanges();
            }
        }

        // Listings filed straight under "Maszyny" with no subtype at all: construction is the
        // closest honest default, and they stay visible/reassignable in the admin panel.
        var looseAdverts = db.CarAdverts.Where(a => a.VehicleCategoryId == src.Id).ToList();
        foreach (var a in looseAdverts) { a.VehicleCategoryId = construction.Id; movedAdverts++; }
        if (looseAdverts.Count > 0) db.SaveChanges();

        // Feature groups and attribute definitions hold RESTRICT foreign keys to the category, so
        // they must be relocated (or dropped when the destination already defines the same thing)
        // before the category can go.
        foreach (var fc in db.FeatureCategories.Where(fc => fc.VehicleCategoryId == src.Id).ToList())
        {
            var twin = db.FeatureCategories
                .Where(x => x.VehicleCategoryId == construction.Id)
                .AsEnumerable()
                .FirstOrDefault(x => NKey(x.Name) == NKey(fc.Name));
            if (twin is null) fc.VehicleCategoryId = construction.Id;
            else
            {
                foreach (var f in db.Features.Where(f => f.CategoryId == fc.Id)) f.CategoryId = twin.Id;
                db.SaveChanges();
                db.FeatureCategories.Remove(fc);
            }
            db.SaveChanges();
        }

        foreach (var def in db.AttributeDefinitions.Where(d => d.VehicleCategoryId == src.Id).ToList())
        {
            var clash = db.AttributeDefinitions.Any(d =>
                d.VehicleCategoryId == construction.Id && d.VehicleSubtypeId == null &&
                d.BrandId == null && d.ModelId == null && d.GenerationId == null && d.TrimId == null &&
                d.Key == def.Key);

            if (!clash) { def.VehicleCategoryId = construction.Id; db.SaveChanges(); continue; }

            // Destination already defines this field. Drop ours, but only if no listing stored a
            // value against it - otherwise keep it so no data is lost.
            if (db.AdvertAttributeValues.Any(v => v.AttributeDefinitionId == def.Id))
            {
                logger.LogWarning("[Consolidate] Attribute '{Key}' kept on Maszyny - it holds stored values and budowlane already defines it", def.Key);
                continue;
            }
            db.AttributeDefinitions.Remove(def);
            db.SaveChanges();
        }

        // Brand links: give the brands to construction, then release the old rows.
        db.Database.ExecuteSqlRaw(@"
            INSERT IGNORE INTO `brandvehiclecategories` (`BrandsId`, `CategoriesId`)
            SELECT `BrandsId`, {0} FROM `brandvehiclecategories` WHERE `CategoriesId` = {1}",
            construction.Id, src.Id);
        db.Database.ExecuteSqlRaw("DELETE FROM `brandvehiclecategories` WHERE `CategoriesId` = {0}", src.Id);

        // Model scoping rows from Etap 3 point at the old category too.
        db.Database.ExecuteSqlRaw(@"
            INSERT IGNORE INTO `modelvehiclecategories` (`ModelsId`, `CategoriesId`)
            SELECT `ModelsId`, {0} FROM `modelvehiclecategories` WHERE `CategoriesId` = {1}",
            construction.Id, src.Id);
        db.Database.ExecuteSqlRaw("DELETE FROM `modelvehiclecategories` WHERE `CategoriesId` = {0}", src.Id);

        // Only now, and only if genuinely nothing points at it any more.
        var blockers = new List<string>();
        if (db.CarAdverts.Any(a => a.VehicleCategoryId == src.Id)) blockers.Add("adverts");
        if (db.VehicleSubtypes.Any(v => v.VehicleCategoryId == src.Id)) blockers.Add("subtypes");
        if (db.FeatureCategories.Any(f => f.VehicleCategoryId == src.Id)) blockers.Add("feature groups");
        if (db.AttributeDefinitions.Any(d => d.VehicleCategoryId == src.Id)) blockers.Add("attribute definitions");

        if (blockers.Count > 0)
        {
            logger.LogWarning(
                "[Consolidate] Category 'maszyny' still referenced by {Blockers} - moved {Moved} advert(s) but kept the category",
                string.Join(", ", blockers), movedAdverts);
            return;
        }

        db.VehicleCategories.Remove(src);
        db.SaveChanges();
        logger.LogInformation(
            "[Consolidate] Retired category 'maszyny': {Moved} advert(s) moved to budowlane/wozki-widlowe", movedAdverts);
    }
}
