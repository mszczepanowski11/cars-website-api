using CarsWebsite;
using cars_website_api.CarsWebsite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Data;

/// <summary>
/// Adds brands, models, generations and real engine specs for categories missing from the initial seeders.
/// Fully idempotent: per-brand and per-generation checks, safe to run on every startup.
/// </summary>
public static class ComprehensiveSeeder
{
    // Matches "Generation I", "Gen 1", etc. AND the same with a year-range suffix like
    // "Generation I (2011–2022)" or "Gen I (2016–)" — the exact-string list used to miss the
    // year-suffixed form, which is how these placeholder names actually show up in the DB
    // (e.g. Lamborghini Aventador's un-fixed "Generation I (2011–2022)"), so
    // GetOrFixGeneration's rename-in-place path never triggered and it created a duplicate
    // generation instead of fixing the existing (wrong-engine) one in place.
    //
    // IMPORTANT: bare Roman numerals ("I", "II"...) are matched WITHOUT a year suffix only —
    // several models (e.g. Bentley Continental GT below) deliberately use bare
    // "I (2003–2011)"/"II (2011–2018)" as real, final generation names, so allowing a suffix
    // on the bare form would make GetOrFixGeneration's orphan cleanup delete sibling
    // generations as false "generic" matches.
    private static readonly System.Text.RegularExpressions.Regex GenericGenNameRegex = new(
        @"^(?:(?:Generation|Gen\.?)\s*(?:I{1,3}|IV|V|[0-9]+)(\s*\([^)]*\))?|I{1,3}|IV|V)$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static void SeedComprehensiveData(AppDbContext db, ILogger logger)
    {
        logger.LogWarning("[STARTUP-TRACE] ComprehensiveSeeder.SeedComprehensiveData entered");
        // GroupBy+First instead of ToDictionary: duplicate Brand/FuelType names (e.g. a
        // stray second "Krone" row) must not crash the whole seeder on the very first line.
        // Keep the lowest-Id row per name — the original, most likely to have existing
        // Model/Generation/EngineVersion rows already pointing at it.
        var brandDict = db.Brands.Include(b => b.Categories).AsEnumerable()
            .GroupBy(b => b.Name).ToDictionary(g => g.Key, g => g.OrderBy(b => b.Id).First());
        var fuelDict  = db.FuelTypes.AsEnumerable()
            .GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.OrderBy(f => f.Id).First().Id);
        var seededModelBrandIds = db.Models.Select(m => m.BrandId).Distinct().ToHashSet();
        var allVCats = db.VehicleCategories.ToList();

        int CatId(string slug) => allVCats.FirstOrDefault(c => c.Slug == slug)?.Id ?? 0;

        int GetFuel(string name) => fuelDict.TryGetValue(name, out var id) ? id : 0;

        // Get-or-create: the live FuelTypes table doesn't have a plain "Hybryda" (full,
        // non-plug-in hybrid) row — only "Hybryda plug-in (PHEV)" and "Hybryda mild (MHEV)" —
        // so it's created here once instead of mislabelling every full-hybrid engine as Benzyna.
        int GetOrCreateFuel(string name)
        {
            if (fuelDict.TryGetValue(name, out var id)) return id;
            var ft = new FuelType { Name = name };
            db.FuelTypes.Add(ft);
            db.SaveChanges();
            fuelDict[name] = ft.Id;
            logger.LogWarning("[STARTUP-TRACE] ComprehensiveSeeder: created missing FuelType '{Name}' (id={Id})", name, ft.Id);
            return ft.Id;
        }

        // Names must match the live FuelTypes table exactly — confirmed via ModelSeeder's
        // [STARTUP-TRACE] fuels-in-DB dump: Diesel, Benzyna, Gaz, Elektryczny,
        // "Hybryda plug-in (PHEV)", LPG, "Hybryda mild (MHEV)", Wodór.
        int ben  = GetFuel("Benzyna");
        int die  = GetFuel("Diesel");
        int phev = GetFuel("Hybryda plug-in (PHEV)");
        int ev   = GetFuel("Elektryczny");
        int lpg  = GetFuel("LPG");
        int mild = GetFuel("Hybryda mild (MHEV)");
        int hyb  = GetOrCreateFuel("Hybryda");

        // Defense in depth: a missing fuel-type name must not crash this seeder via an FK
        // violation on EngineVersion.FuelTypeId — fall back to Benzyna and log loudly instead.
        if (ben == 0 && fuelDict.Count > 0) ben = fuelDict.Values.First();
        if (die == 0) { logger.LogError("[STARTUP-TRACE] ComprehensiveSeeder: FuelType 'Diesel' missing — falling back to Benzyna"); die = ben; }
        if (phev == 0) { logger.LogError("[STARTUP-TRACE] ComprehensiveSeeder: FuelType 'Hybryda plug-in (PHEV)' missing — falling back to Benzyna"); phev = ben; }
        if (ev == 0) { logger.LogError("[STARTUP-TRACE] ComprehensiveSeeder: FuelType 'Elektryczny' missing — falling back to Benzyna"); ev = ben; }
        if (lpg == 0) { logger.LogError("[STARTUP-TRACE] ComprehensiveSeeder: FuelType 'LPG' missing — falling back to Benzyna"); lpg = ben; }
        if (mild == 0) { logger.LogError("[STARTUP-TRACE] ComprehensiveSeeder: FuelType 'Hybryda mild (MHEV)' missing — falling back to Benzyna"); mild = ben; }

        int GetOrCreateBrand(string name, string slug, params string[] categorySlugs)
        {
            if (brandDict.TryGetValue(name, out var existing)) return existing.Id;
            var cats = allVCats.Where(c => categorySlugs.Contains(c.Slug)).ToList();
            var brand = new Brand { Name = name, Slug = slug, Categories = cats };
            db.Brands.Add(brand);
            db.SaveChanges();
            brandDict[name] = brand;
            return brand.Id;
        }

        int GetOrCreateModel(int brandId, string name, string slug)
        {
            var m = db.Models.FirstOrDefault(x => x.BrandId == brandId && x.Name == name);
            if (m != null) return m.Id;
            m = new Model { BrandId = brandId, Name = name, Slug = slug };
            db.Models.Add(m);
            db.SaveChanges();
            return m.Id;
        }

        // GetOrCreateGeneration used to be a plain exact-name lookup with no fallback: if a model
        // already had a broken placeholder generation (e.g. "Generation I") under a different
        // name, this created a brand-new duplicate generation and silently left the placeholder —
        // and its wrong engine data — untouched and still visible in the form. Confirmed via the
        // AUDIT log: models with real curated data here (Giulia, Stelvio, Tonale, Giulietta, 147,
        // 156, MiTo, and surely many more throughout this file) were STILL showing up as broken
        // because of this. GetOrFixGeneration is a strict superset of the old behavior — identical
        // when there's no existing generation to fix, but correctly reuses/renames a placeholder
        // in place instead of duplicating it — so every existing call site benefits automatically.
        int GetOrCreateGeneration(int modelId, string name, string slug, int yearFrom, int? yearTo)
            => GetOrFixGeneration(modelId, name, slug, yearFrom, yearTo);

        static bool IsGenericGenName(string n) => GenericGenNameRegex.IsMatch(n.Trim());

        // Maps existing generic placeholder generations (e.g. "Generation I/II/III") to real names
        // in creation order, preserving all FK references (adverts, trims, etc.).
        // Call this BEFORE GetOrFixGeneration for any model with known multi-gen history.
        void PrepareGenerations(int modelId, params (string name, string slug, int yearFrom, int? yearTo)[] expected)
        {
            var existing = db.Generations.Where(x => x.ModelId == modelId).ToList();
            var alreadyNamed = existing.Select(x => x.Name).ToHashSet();
            var generics = existing.Where(x => IsGenericGenName(x.Name)).OrderBy(x => x.Id).ToList();
            if (!generics.Any()) return;

            // Only rename generics whose target name isn't already taken
            var toRename = expected.Where(e => !alreadyNamed.Contains(e.name)).ToList();

            for (int i = 0; i < Math.Min(generics.Count, toRename.Count); i++)
            {
                var g = generics[i];
                var exp = toRename[i];
                logger.LogInformation("[ComprehensiveSeeder] PrepareGenerations: '{Old}' → '{New}'", g.Name, exp.name);
                g.Name = exp.name; g.Slug = exp.slug; g.YearFrom = exp.yearFrom; g.YearTo = exp.yearTo;
            }

            // Delete any leftover generics that couldn't be mapped (wrong count)
            for (int i = toRename.Count; i < generics.Count; i++)
            {
                var orphan = generics[i];
                // Nullify FK in CarAdverts before deleting via the tracked DbSet, not raw SQL with
                // a hardcoded table name — see the identical fix in GetOrFixGeneration below for why.
                foreach (var a in db.CarAdverts.Where(a => a.GenerationId == orphan.Id)) a.GenerationId = null;
                var engines = db.EngineVersions.Where(e => e.GenerationId == orphan.Id).ToList();
                db.EngineVersions.RemoveRange(engines);
                db.Generations.Remove(orphan);
                logger.LogInformation("[ComprehensiveSeeder] PrepareGenerations: deleted extra generic gen '{Name}'", orphan.Name);
            }

            db.SaveChanges();
        }

        // Like GetOrCreateGeneration, but if the model has exactly one generation with a generic
        // placeholder name (e.g. "Generation I"), renames it in place to preserve existing FK refs.
        // Also deletes any leftover generic-named orphan generations once the correct one exists.
        int GetOrFixGeneration(int modelId, string name, string slug, int yearFrom, int? yearTo)
        {
            var g = db.Generations.FirstOrDefault(x => x.ModelId == modelId && x.Name == name);
            if (g != null)
            {
                // Remove stale generic orphans that may have accumulated alongside the real generation
                var orphans = db.Generations
                    .Where(x => x.ModelId == modelId && x.Id != g.Id)
                    .ToList()
                    .Where(x => IsGenericGenName(x.Name))
                    .ToList();
                if (orphans.Any())
                {
                    foreach (var o in orphans)
                    {
                        // Nullify FK in CarAdverts before deleting — the migration defined this FK without ON DELETE,
                        // so MySQL defaults to RESTRICT and would block the delete otherwise. Use the tracked
                        // DbSet (not raw SQL with a hardcoded table name) — this codebase's live schema uses
                        // lowercase table names that don't match the PascalCase migration history, so a
                        // hardcoded "CarAdverts" string throws "Table 'railway.CarAdverts' doesn't exist" and
                        // aborts every seeder queued after this one, same bug MergeDuplicateBrands had (#50).
                        foreach (var a in db.CarAdverts.Where(a => a.GenerationId == o.Id)) a.GenerationId = null;
                        var orphanEngines = db.EngineVersions.Where(e => e.GenerationId == o.Id).ToList();
                        db.EngineVersions.RemoveRange(orphanEngines);
                        db.Generations.Remove(o);
                        logger.LogInformation("[ComprehensiveSeeder] Deleted orphan generation '{Name}' (id={Id}) from model {ModelId}", o.Name, o.Id, modelId);
                    }
                    db.SaveChanges();
                }
                return g.Id;
            }

            var allGens = db.Generations.Where(x => x.ModelId == modelId).ToList();
            if (allGens.Count == 1)
            {
                var sole = allGens[0];
                if (IsGenericGenName(sole.Name))
                {
                    sole.Name    = name;
                    sole.Slug    = slug;
                    sole.YearFrom = yearFrom;
                    sole.YearTo   = yearTo;
                    db.SaveChanges();
                    logger.LogInformation("[ComprehensiveSeeder] Renamed placeholder gen to '{Name}'", name);
                    return sole.Id;
                }
            }

            g = new Generation { ModelId = modelId, Name = name, Slug = slug, YearFrom = yearFrom, YearTo = yearTo };
            db.Generations.Add(g);
            db.SaveChanges();
            return g.Id;
        }

        // Deleting an EngineVersion that's still referenced by a real CarAdvert violates
        // FK_CarAdverts_EngineVersions_EngineVersionId (no ON DELETE CASCADE/SET NULL
        // configured on that FK) — detach any such adverts first so the correct engine data
        // can actually replace the wrong rows instead of crashing the whole seeder chain.
        void SafeRemoveEngines(List<EngineVersion> toRemove)
        {
            if (toRemove.Count == 0) return;
            var ids = toRemove.Select(e => e.Id).ToList();
            var blockingAdverts = db.CarAdverts.Where(a => a.EngineVersionId != null && ids.Contains(a.EngineVersionId.Value)).ToList();
            if (blockingAdverts.Any())
            {
                foreach (var a in blockingAdverts) a.EngineVersionId = null;
                db.SaveChanges();
                logger.LogWarning("[ComprehensiveSeeder] Detached {Count} CarAdverts from engines being replaced", blockingAdverts.Count);
            }
            db.EngineVersions.RemoveRange(toRemove);
            db.SaveChanges();
        }

        void AddEngines(int generationId, List<EngineVersion> engines)
        {
            var expectedNames = engines.Select(e => e.EngineName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var wrongNames = db.EngineVersions
                .Where(e => e.GenerationId == generationId)
                .ToList()
                .Where(e => !expectedNames.Contains(e.EngineName))
                .ToList();
            if (wrongNames.Any())
            {
                SafeRemoveEngines(wrongNames);
                logger.LogInformation("[ComprehensiveSeeder] Removed {Count} unexpected engines from gen {Id}", wrongNames.Count, generationId);
            }
            if (db.EngineVersions.Any(e => e.GenerationId == generationId && e.TorqueNm != null)) return;
            foreach (var e in engines) e.GenerationId = generationId;
            db.EngineVersions.AddRange(engines);
            db.SaveChanges();
        }

        // Removes engines with HP below minExpectedHp OR with an unexpected name, then adds correct ones.
        // This ensures wrong engines can never block the correct set, regardless of their TorqueNm state.
        void AddOrReplaceEngines(int generationId, int minExpectedHp, List<EngineVersion> engines)
        {
            var expectedNames = engines.Select(e => e.EngineName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var wrong = db.EngineVersions
                .Where(e => e.GenerationId == generationId)
                .ToList()
                .Where(e => e.PowerHP < minExpectedHp || !expectedNames.Contains(e.EngineName))
                .ToList();
            if (wrong.Any())
            {
                SafeRemoveEngines(wrong);
                logger.LogInformation("[ComprehensiveSeeder] Removed {Count} wrong engines from gen {Id}", wrong.Count, generationId);
            }
            if (db.EngineVersions.Any(e => e.GenerationId == generationId && e.TorqueNm != null)) return;
            foreach (var e in engines) e.GenerationId = generationId;
            db.EngineVersions.AddRange(engines);
            db.SaveChanges();
        }

        // Removes ALL existing engines for the generation, then inserts the correct ones.
        // Use for ultra-premium/exotic brands where any wrong engine is unambiguously wrong.
        void ForceReplaceEngines(int generationId, List<EngineVersion> engines)
        {
            var existing = db.EngineVersions.Where(e => e.GenerationId == generationId).ToList();
            bool alreadyCorrect = existing.Count == engines.Count
                && existing.All(e => engines.Any(n => n.EngineName == e.EngineName && e.TorqueNm != null));
            if (alreadyCorrect) return;
            if (existing.Any())
            {
                SafeRemoveEngines(existing);
                logger.LogInformation("[ComprehensiveSeeder] ForceReplace: removed {Count} engines from gen {Id}", existing.Count, generationId);
            }
            foreach (var e in engines) e.GenerationId = generationId;
            db.EngineVersions.AddRange(engines);
            db.SaveChanges();
        }

        bool BrandNeedsModels(int brandId) => !seededModelBrandIds.Contains(brandId);

        // ── BUGATTI ─────────────────────────────────────────────────────────────
        {
            int bId = GetOrCreateBrand("Bugatti", "bugatti", "auta-osobowe");
            if (BrandNeedsModels(bId)) { seededModelBrandIds.Add(bId); }

            int veyron = GetOrCreateModel(bId, "Veyron", "bugatti-veyron");
            ForceReplaceEngines(GetOrFixGeneration(veyron, "16.4 (2005–2015)", "bugatti-veyron-164", 2005, 2015), [
                new EngineVersion { EngineName = "W16 8.0 1001 KM", PowerHP = 1001, PowerKW = 736, Displacement = 7993, FuelTypeId = ben,
                    TorqueNm = 1250, Co2EmissionGkm = 574, EuroNorm = "Euro 4", GearboxType = "dsg",
                    DriveType = "AWD", Cylinders = 16, Acceleration0100 = 2.5m, TopSpeedKmh = 407,
                    FuelConsumptionCombined = 24.1m },
                new EngineVersion { EngineName = "W16 8.0 SS 1200 KM", PowerHP = 1200, PowerKW = 882, Displacement = 7993, FuelTypeId = ben,
                    TorqueNm = 1500, Co2EmissionGkm = 574, EuroNorm = "Euro 5", GearboxType = "dsg",
                    DriveType = "AWD", Cylinders = 16, Acceleration0100 = 2.4m, TopSpeedKmh = 431,
                    FuelConsumptionCombined = 26.0m },
            ]);

            int chiron = GetOrCreateModel(bId, "Chiron", "bugatti-chiron");
            ForceReplaceEngines(GetOrFixGeneration(chiron, "Chiron (2016–2023)", "bugatti-chiron-2016", 2016, 2023), [
                new EngineVersion { EngineName = "W16 8.0 1500 KM", PowerHP = 1500, PowerKW = 1103, Displacement = 7993, FuelTypeId = ben,
                    TorqueNm = 1600, Co2EmissionGkm = 516, EuroNorm = "Euro 6", GearboxType = "dsg",
                    DriveType = "AWD", Cylinders = 16, Acceleration0100 = 2.4m, TopSpeedKmh = 420,
                    FuelConsumptionCombined = 22.5m },
                new EngineVersion { EngineName = "W16 8.0 Pur Sport 1500 KM", PowerHP = 1500, PowerKW = 1103, Displacement = 7993, FuelTypeId = ben,
                    TorqueNm = 1600, Co2EmissionGkm = 516, EuroNorm = "Euro 6", GearboxType = "dsg",
                    DriveType = "AWD", Cylinders = 16, Acceleration0100 = 2.3m, TopSpeedKmh = 350,
                    FuelConsumptionCombined = 22.5m },
                new EngineVersion { EngineName = "W16 8.0 Super Sport 300+ 1600 KM", PowerHP = 1600, PowerKW = 1176, Displacement = 7993, FuelTypeId = ben,
                    TorqueNm = 1600, Co2EmissionGkm = 516, EuroNorm = "Euro 6", GearboxType = "dsg",
                    DriveType = "AWD", Cylinders = 16, Acceleration0100 = 2.3m, TopSpeedKmh = 490,
                    FuelConsumptionCombined = 23.0m },
            ]);

            int bolide = GetOrCreateModel(bId, "Bolide", "bugatti-bolide");
            ForceReplaceEngines(GetOrFixGeneration(bolide, "Bolide (2024–)", "bugatti-bolide-2024", 2024, null), [
                new EngineVersion { EngineName = "W16 8.0 1825 KM", PowerHP = 1825, PowerKW = 1342, Displacement = 7993, FuelTypeId = ben,
                    TorqueNm = 1850, EuroNorm = "Euro 6d", GearboxType = "dsg",
                    DriveType = "AWD", Cylinders = 16, Acceleration0100 = 2.2m, TopSpeedKmh = 500,
                    FuelConsumptionCombined = 24.0m },
            ]);

            int divo = GetOrCreateModel(bId, "Divo", "bugatti-divo");
            ForceReplaceEngines(GetOrFixGeneration(divo, "Divo (2019–2021)", "bugatti-divo-2019", 2019, 2021), [
                new EngineVersion { EngineName = "W16 8.0 1479 KM", PowerHP = 1479, PowerKW = 1088, Displacement = 7993, FuelTypeId = ben,
                    TorqueNm = 1600, EuroNorm = "Euro 6d", GearboxType = "dsg",
                    DriveType = "AWD", Cylinders = 16, Acceleration0100 = 2.4m, TopSpeedKmh = 380,
                    FuelConsumptionCombined = 23.0m },
            ]);

            int centodieci = GetOrCreateModel(bId, "Centodieci", "bugatti-centodieci");
            ForceReplaceEngines(GetOrFixGeneration(centodieci, "Centodieci (2022–)", "bugatti-centodieci-2022", 2022, null), [
                new EngineVersion { EngineName = "W16 8.0 1577 KM", PowerHP = 1577, PowerKW = 1161, Displacement = 7993, FuelTypeId = ben,
                    TorqueNm = 1600, EuroNorm = "Euro 6d", GearboxType = "dsg",
                    DriveType = "AWD", Cylinders = 16, Acceleration0100 = 2.4m, TopSpeedKmh = 380,
                    FuelConsumptionCombined = 23.5m },
            ]);

            int laVoitureNoire = GetOrCreateModel(bId, "La Voiture Noire", "bugatti-la-voiture-noire");
            ForceReplaceEngines(GetOrFixGeneration(laVoitureNoire, "La Voiture Noire (2021–)", "bugatti-la-voiture-noire-2021", 2021, null), [
                new EngineVersion { EngineName = "W16 8.0 1479 KM", PowerHP = 1479, PowerKW = 1088, Displacement = 7993, FuelTypeId = ben,
                    TorqueNm = 1600, EuroNorm = "Euro 6d", GearboxType = "dsg",
                    DriveType = "AWD", Cylinders = 16, Acceleration0100 = 2.4m, TopSpeedKmh = 420,
                    FuelConsumptionCombined = 23.0m },
            ]);

            int mistral = GetOrCreateModel(bId, "Mistral", "bugatti-mistral");
            ForceReplaceEngines(GetOrFixGeneration(mistral, "Mistral (2024–)", "bugatti-mistral-2024", 2024, null), [
                new EngineVersion { EngineName = "W16 8.0 1577 KM", PowerHP = 1577, PowerKW = 1161, Displacement = 7993, FuelTypeId = ben,
                    TorqueNm = 1600, EuroNorm = "Euro 6d", GearboxType = "dsg",
                    DriveType = "AWD", Cylinders = 16, Acceleration0100 = 2.3m, TopSpeedKmh = 420,
                    FuelConsumptionCombined = 24.0m },
            ]);

            int tourbillon = GetOrCreateModel(bId, "Tourbillon", "bugatti-tourbillon");
            ForceReplaceEngines(GetOrFixGeneration(tourbillon, "Tourbillon (2026–)", "bugatti-tourbillon-2026", 2026, null), [
                new EngineVersion { EngineName = "V16 8.3 Hybrid 1800 KM", PowerHP = 1800, PowerKW = 1324, Displacement = 8260, FuelTypeId = hyb,
                    TorqueNm = 1800, EuroNorm = "Euro 6d", GearboxType = "dsg",
                    DriveType = "AWD", Cylinders = 16, Acceleration0100 = 2.0m, TopSpeedKmh = 445,
                    FuelConsumptionCombined = 18.0m },
            ]);

            int eb110 = GetOrCreateModel(bId, "EB110", "bugatti-eb110");
            ForceReplaceEngines(GetOrFixGeneration(eb110, "EB110 (1991–1995)", "bugatti-eb110-1991", 1991, 1995), [
                new EngineVersion { EngineName = "V12 3.5 GT 553 KM", PowerHP = 553, PowerKW = 408, Displacement = 3499, FuelTypeId = ben,
                    TorqueNm = 611, EuroNorm = "Euro 1", GearboxType = "manual",
                    DriveType = "AWD", Cylinders = 12, Acceleration0100 = 4.4m, TopSpeedKmh = 342,
                    FuelConsumptionCombined = 18.5m },
                new EngineVersion { EngineName = "V12 3.5 SS 603 KM", PowerHP = 603, PowerKW = 444, Displacement = 3499, FuelTypeId = ben,
                    TorqueNm = 650, EuroNorm = "Euro 1", GearboxType = "manual",
                    DriveType = "AWD", Cylinders = 12, Acceleration0100 = 3.2m, TopSpeedKmh = 351,
                    FuelConsumptionCombined = 19.0m },
            ]);

            int eb16 = GetOrCreateModel(bId, "EB16/4 Veyron", "bugatti-eb164-concept");
            ForceReplaceEngines(GetOrFixGeneration(eb16, "EB16/4 Veyron (1999–2001)", "bugatti-eb164-1999", 1999, 2001), [
                new EngineVersion { EngineName = "W16 7.3 Concept 736 KM", PowerHP = 736, PowerKW = 541, Displacement = 7300, FuelTypeId = ben,
                    TorqueNm = 1000, EuroNorm = "Euro 3", GearboxType = "dsg",
                    DriveType = "AWD", Cylinders = 16, Acceleration0100 = 3.0m, TopSpeedKmh = 400,
                    FuelConsumptionCombined = 25.0m },
            ]);
        }

        // ── ROLLS-ROYCE ─────────────────────────────────────────────────────────
        {
            int bId = GetOrCreateBrand("Rolls-Royce", "rolls-royce", "auta-osobowe");
            if (BrandNeedsModels(bId)) { seededModelBrandIds.Add(bId); }

            int ghost = GetOrCreateModel(bId, "Ghost", "rr-ghost");
            PrepareGenerations(ghost,
                ("Ghost I (2009–2020)", "rr-ghost-i", 2009, 2020),
                ("Ghost II (2020–)", "rr-ghost-ii", 2020, null));
            AddOrReplaceEngines(GetOrFixGeneration(ghost, "Ghost I (2009–2020)", "rr-ghost-i", 2009, 2020), 400, [
                new EngineVersion { EngineName = "6.75 V12 570 KM", PowerHP = 570, PowerKW = 419, Displacement = 6749, FuelTypeId = ben,
                    TorqueNm = 780, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 4.9m, TopSpeedKmh = 250, FuelConsumptionCombined = 15.3m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(ghost, "Ghost II (2020–)", "rr-ghost-ii", 2020, null), 400, [
                new EngineVersion { EngineName = "6.75 V12 571 KM", PowerHP = 571, PowerKW = 420, Displacement = 6749, FuelTypeId = ben,
                    TorqueNm = 850, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 4.8m, TopSpeedKmh = 250, FuelConsumptionCombined = 14.9m },
                new EngineVersion { EngineName = "6.75 V12 Black Badge 600 KM", PowerHP = 600, PowerKW = 441, Displacement = 6749, FuelTypeId = ben,
                    TorqueNm = 900, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 4.5m, TopSpeedKmh = 250, FuelConsumptionCombined = 15.2m },
            ]);

            int phantom = GetOrCreateModel(bId, "Phantom", "rr-phantom");
            PrepareGenerations(phantom,
                ("Phantom VII (2003–2016)", "rr-phantom-vii", 2003, 2016),
                ("Phantom VIII (2017–)", "rr-phantom-viii", 2017, null));
            AddOrReplaceEngines(GetOrFixGeneration(phantom, "Phantom VII (2003–2016)", "rr-phantom-vii", 2003, 2016), 400, [
                new EngineVersion { EngineName = "6.75 V12 460 KM", PowerHP = 460, PowerKW = 338, Displacement = 6749, FuelTypeId = ben,
                    TorqueNm = 720, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 5.9m, TopSpeedKmh = 240, FuelConsumptionCombined = 16.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(phantom, "Phantom VIII (2017–)", "rr-phantom-viii", 2017, null), 400, [
                new EngineVersion { EngineName = "6.75 V12 571 KM", PowerHP = 571, PowerKW = 420, Displacement = 6749, FuelTypeId = ben,
                    TorqueNm = 900, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 5.1m, TopSpeedKmh = 250, FuelConsumptionCombined = 15.8m },
            ]);

            int wraith = GetOrCreateModel(bId, "Wraith", "rr-wraith");
            AddOrReplaceEngines(GetOrFixGeneration(wraith, "Wraith (2013–2023)", "rr-wraith-2013", 2013, 2023), 400, [
                new EngineVersion { EngineName = "6.6 V12 Bi-Turbo 632 KM", PowerHP = 632, PowerKW = 465, Displacement = 6592, FuelTypeId = ben,
                    TorqueNm = 800, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 4.4m, TopSpeedKmh = 250, FuelConsumptionCombined = 14.9m },
            ]);

            int dawn = GetOrCreateModel(bId, "Dawn", "rr-dawn");
            AddOrReplaceEngines(GetOrFixGeneration(dawn, "Dawn (2015–2023)", "rr-dawn-2015", 2015, 2023), 400, [
                new EngineVersion { EngineName = "6.6 V12 Bi-Turbo 571 KM", PowerHP = 571, PowerKW = 420, Displacement = 6592, FuelTypeId = ben,
                    TorqueNm = 820, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 5.0m, TopSpeedKmh = 250, FuelConsumptionCombined = 15.4m },
            ]);

            int cullinan = GetOrCreateModel(bId, "Cullinan", "rr-cullinan");
            AddOrReplaceEngines(GetOrFixGeneration(cullinan, "Cullinan (2018–)", "rr-cullinan-2018", 2018, null), 400, [
                new EngineVersion { EngineName = "6.75 V12 571 KM", PowerHP = 571, PowerKW = 420, Displacement = 6749, FuelTypeId = ben,
                    TorqueNm = 850, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 5.2m, TopSpeedKmh = 250, FuelConsumptionCombined = 15.7m },
                new EngineVersion { EngineName = "6.75 V12 Black Badge 600 KM", PowerHP = 600, PowerKW = 441, Displacement = 6749, FuelTypeId = ben,
                    TorqueNm = 900, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 4.9m, TopSpeedKmh = 250, FuelConsumptionCombined = 16.0m },
            ]);

            int spectre = GetOrCreateModel(bId, "Spectre", "rr-spectre");
            AddOrReplaceEngines(GetOrFixGeneration(spectre, "Spectre (2023–)", "rr-spectre-2023", 2023, null), 400, [
                new EngineVersion { EngineName = "Elektryczny AWD 585 KM", PowerHP = 585, PowerKW = 430, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 900, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 4.5m, TopSpeedKmh = 250, FuelConsumptionCombined = 0 },
            ]);
        }

        // ── BENTLEY ─────────────────────────────────────────────────────────────
        {
            int bId = GetOrCreateBrand("Bentley", "bentley", "auta-osobowe");
            if (BrandNeedsModels(bId)) { seededModelBrandIds.Add(bId); }

            int cgt = GetOrCreateModel(bId, "Continental GT", "bentley-continental-gt");
            PrepareGenerations(cgt,
                ("I (2003–2011)", "bentley-cgt-i", 2003, 2011),
                ("II (2011–2018)", "bentley-cgt-ii", 2011, 2018),
                ("III (2018–)", "bentley-cgt-iii", 2018, null));
            AddOrReplaceEngines(GetOrFixGeneration(cgt, "I (2003–2011)", "bentley-cgt-i", 2003, 2011), 400, [
                new EngineVersion { EngineName = "6.0 W12 560 KM", PowerHP = 560, PowerKW = 412, Displacement = 5998, FuelTypeId = ben,
                    TorqueNm = 650, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 4.8m, TopSpeedKmh = 318, FuelConsumptionCombined = 17.0m },
                new EngineVersion { EngineName = "6.0 W12 Speed 610 KM", PowerHP = 610, PowerKW = 449, Displacement = 5998, FuelTypeId = ben,
                    TorqueNm = 750, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 4.3m, TopSpeedKmh = 330, FuelConsumptionCombined = 17.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(cgt, "II (2011–2018)", "bentley-cgt-ii", 2011, 2018), 400, [
                new EngineVersion { EngineName = "6.0 W12 575 KM", PowerHP = 575, PowerKW = 423, Displacement = 5998, FuelTypeId = ben,
                    TorqueNm = 800, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 4.3m, TopSpeedKmh = 318, FuelConsumptionCombined = 15.4m },
                new EngineVersion { EngineName = "4.0 V8 507 KM", PowerHP = 507, PowerKW = 373, Displacement = 3993, FuelTypeId = ben,
                    TorqueNm = 660, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.6m, TopSpeedKmh = 303, FuelConsumptionCombined = 12.0m },
                new EngineVersion { EngineName = "4.0 V8 S 521 KM", PowerHP = 521, PowerKW = 383, Displacement = 3993, FuelTypeId = ben,
                    TorqueNm = 680, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.5m, TopSpeedKmh = 309, FuelConsumptionCombined = 12.3m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(cgt, "III (2018–)", "bentley-cgt-iii", 2018, null), 400, [
                new EngineVersion { EngineName = "6.0 W12 635 KM", PowerHP = 635, PowerKW = 467, Displacement = 5950, FuelTypeId = ben,
                    TorqueNm = 900, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 3.6m, TopSpeedKmh = 335, FuelConsumptionCombined = 14.8m },
                new EngineVersion { EngineName = "4.0 V8 550 KM", PowerHP = 550, PowerKW = 404, Displacement = 3996, FuelTypeId = ben,
                    TorqueNm = 770, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 3.9m, TopSpeedKmh = 318, FuelConsumptionCombined = 12.4m },
                new EngineVersion { EngineName = "6.0 W12 Speed 659 KM", PowerHP = 659, PowerKW = 485, Displacement = 5950, FuelTypeId = ben,
                    TorqueNm = 900, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 3.5m, TopSpeedKmh = 335, FuelConsumptionCombined = 15.2m },
            ]);

            int bentayga = GetOrCreateModel(bId, "Bentayga", "bentley-bentayga");
            PrepareGenerations(bentayga,
                ("I (2015–2020)", "bentley-bentayga-i", 2015, 2020),
                ("II (2020–)", "bentley-bentayga-ii", 2020, null));
            AddOrReplaceEngines(GetOrFixGeneration(bentayga, "I (2015–2020)", "bentley-bentayga-i", 2015, 2020), 400, [
                new EngineVersion { EngineName = "6.0 W12 608 KM", PowerHP = 608, PowerKW = 447, Displacement = 5950, FuelTypeId = ben,
                    TorqueNm = 900, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 4.1m, TopSpeedKmh = 301, FuelConsumptionCombined = 15.0m },
                new EngineVersion { EngineName = "4.0 V8 D 435 KM", PowerHP = 435, PowerKW = 320, Displacement = 3956, FuelTypeId = die,
                    TorqueNm = 900, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.8m, TopSpeedKmh = 270, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "3.0 V6 Hybrid 443 KM", PowerHP = 443, PowerKW = 326, Displacement = 2995, FuelTypeId = phev,
                    TorqueNm = 700, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.2m, TopSpeedKmh = 254, FuelConsumptionCombined = 3.4m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(bentayga, "II (2020–)", "bentley-bentayga-ii", 2020, null), 400, [
                new EngineVersion { EngineName = "4.0 V8 550 KM", PowerHP = 550, PowerKW = 404, Displacement = 3996, FuelTypeId = ben,
                    TorqueNm = 770, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.5m, TopSpeedKmh = 290, FuelConsumptionCombined = 12.6m },
                new EngineVersion { EngineName = "6.0 W12 Speed 635 KM", PowerHP = 635, PowerKW = 467, Displacement = 5950, FuelTypeId = ben,
                    TorqueNm = 900, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 3.9m, TopSpeedKmh = 306, FuelConsumptionCombined = 14.3m },
                new EngineVersion { EngineName = "3.0 V6 Hybrid 462 KM", PowerHP = 462, PowerKW = 340, Displacement = 2995, FuelTypeId = phev,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.0m, TopSpeedKmh = 254, FuelConsumptionCombined = 2.7m },
            ]);

            int flyingSpur = GetOrCreateModel(bId, "Flying Spur", "bentley-flying-spur");
            PrepareGenerations(flyingSpur,
                ("I (2003–2014)", "bentley-flying-spur-i", 2003, 2014),
                ("II (2014–2019)", "bentley-flying-spur-ii", 2014, 2019),
                ("III (2019–)", "bentley-flying-spur-iii", 2019, null));
            AddOrReplaceEngines(GetOrFixGeneration(flyingSpur, "I (2003–2014)", "bentley-flying-spur-i", 2003, 2014), 400, [
                new EngineVersion { EngineName = "6.0 W12 552 KM", PowerHP = 552, PowerKW = 406, Displacement = 5950, FuelTypeId = ben,
                    TorqueNm = 650, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 4.9m, TopSpeedKmh = 318, FuelConsumptionCombined = 16.5m },
                new EngineVersion { EngineName = "6.0 W12 Speed 600 KM", PowerHP = 600, PowerKW = 441, Displacement = 5950, FuelTypeId = ben,
                    TorqueNm = 750, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 4.6m, TopSpeedKmh = 322, FuelConsumptionCombined = 17.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(flyingSpur, "II (2014–2019)", "bentley-flying-spur-ii", 2014, 2019), 400, [
                new EngineVersion { EngineName = "6.0 W12 625 KM", PowerHP = 625, PowerKW = 460, Displacement = 5950, FuelTypeId = ben,
                    TorqueNm = 800, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 4.3m, TopSpeedKmh = 322, FuelConsumptionCombined = 15.4m },
                new EngineVersion { EngineName = "4.0 V8 500 KM", PowerHP = 500, PowerKW = 368, Displacement = 3993, FuelTypeId = ben,
                    TorqueNm = 660, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.9m, TopSpeedKmh = 306, FuelConsumptionCombined = 11.9m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(flyingSpur, "III (2019–)", "bentley-flying-spur-iii", 2019, null), 400, [
                new EngineVersion { EngineName = "6.0 W12 635 KM", PowerHP = 635, PowerKW = 467, Displacement = 5950, FuelTypeId = ben,
                    TorqueNm = 900, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 3.8m, TopSpeedKmh = 333, FuelConsumptionCombined = 14.9m },
                new EngineVersion { EngineName = "4.0 V8 550 KM", PowerHP = 550, PowerKW = 404, Displacement = 3996, FuelTypeId = ben,
                    TorqueNm = 770, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.0m, TopSpeedKmh = 318, FuelConsumptionCombined = 12.4m },
                new EngineVersion { EngineName = "2.9 V6 Hybrid 544 KM", PowerHP = 544, PowerKW = 400, Displacement = 2894, FuelTypeId = phev,
                    TorqueNm = 750, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 3.7m, TopSpeedKmh = 285, FuelConsumptionCombined = 2.9m },
            ]);

            int mulsanne = GetOrCreateModel(bId, "Mulsanne", "bentley-mulsanne");
            AddOrReplaceEngines(GetOrFixGeneration(mulsanne, "II (2010–2020)", "bentley-mulsanne-ii", 2010, 2020), 400, [
                new EngineVersion { EngineName = "6.75 V8 512 KM", PowerHP = 512, PowerKW = 377, Displacement = 6749, FuelTypeId = ben,
                    TorqueNm = 1020, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 5.1m, TopSpeedKmh = 296, FuelConsumptionCombined = 17.5m },
                new EngineVersion { EngineName = "6.75 V8 Speed 537 KM", PowerHP = 537, PowerKW = 395, Displacement = 6749, FuelTypeId = ben,
                    TorqueNm = 1100, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 4.8m, TopSpeedKmh = 305, FuelConsumptionCombined = 17.8m },
            ]);

            int arnage = GetOrCreateModel(bId, "Arnage", "bentley-arnage");
            PrepareGenerations(arnage,
                ("I (1998–2002)", "bentley-arnage-i", 1998, 2002),
                ("II (2002–2009)", "bentley-arnage-ii", 2002, 2009));
            AddOrReplaceEngines(GetOrFixGeneration(arnage, "I (1998–2002)", "bentley-arnage-i", 1998, 2002), 300, [
                new EngineVersion { EngineName = "4.4 V8 Twin-Turbo Red Label 350 KM", PowerHP = 350, PowerKW = 257, Displacement = 4398, FuelTypeId = ben,
                    TorqueNm = 630, EuroNorm = "Euro 3", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 6.9m, TopSpeedKmh = 240, FuelConsumptionCombined = 19.5m },
                new EngineVersion { EngineName = "4.4 V8 Twin-Turbo Green Label 400 KM", PowerHP = 400, PowerKW = 294, Displacement = 4398, FuelTypeId = ben,
                    TorqueNm = 700, EuroNorm = "Euro 3", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 6.1m, TopSpeedKmh = 249, FuelConsumptionCombined = 20.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(arnage, "II (2002–2009)", "bentley-arnage-ii", 2002, 2009), 300, [
                new EngineVersion { EngineName = "6.75 V8 Twin-Turbo R 405 KM", PowerHP = 405, PowerKW = 298, Displacement = 6761, FuelTypeId = ben,
                    TorqueNm = 875, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 5.9m, TopSpeedKmh = 249, FuelConsumptionCombined = 20.9m },
                new EngineVersion { EngineName = "6.75 V8 Twin-Turbo T 456 KM", PowerHP = 456, PowerKW = 335, Displacement = 6761, FuelTypeId = ben,
                    TorqueNm = 875, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 5.5m, TopSpeedKmh = 273, FuelConsumptionCombined = 21.5m },
            ]);

            int azure = GetOrCreateModel(bId, "Azure", "bentley-azure");
            PrepareGenerations(azure,
                ("I (1995–2003)", "bentley-azure-i", 1995, 2003),
                ("II (2006–2010)", "bentley-azure-ii", 2006, 2010));
            AddOrReplaceEngines(GetOrFixGeneration(azure, "I (1995–2003)", "bentley-azure-i", 1995, 2003), 300, [
                new EngineVersion { EngineName = "6.75 V8 Turbo 385 KM", PowerHP = 385, PowerKW = 283, Displacement = 6750, FuelTypeId = ben,
                    TorqueNm = 715, EuroNorm = "Euro 2", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 6.6m, TopSpeedKmh = 239, FuelConsumptionCombined = 21.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(azure, "II (2006–2010)", "bentley-azure-ii", 2006, 2010), 400, [
                new EngineVersion { EngineName = "6.75 V8 Twin-Turbo 450 KM", PowerHP = 450, PowerKW = 331, Displacement = 6761, FuelTypeId = ben,
                    TorqueNm = 875, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 5.6m, TopSpeedKmh = 265, FuelConsumptionCombined = 20.9m },
            ]);

            int brooklands = GetOrCreateModel(bId, "Brooklands", "bentley-brooklands");
            AddOrReplaceEngines(GetOrFixGeneration(brooklands, "I (2008–2011)", "bentley-brooklands-i", 2008, 2011), 400, [
                new EngineVersion { EngineName = "6.75 V8 Twin-Turbo 530 KM", PowerHP = 530, PowerKW = 390, Displacement = 6761, FuelTypeId = ben,
                    TorqueNm = 1050, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 5.3m, TopSpeedKmh = 296, FuelConsumptionCombined = 20.8m },
            ]);

            int cgtc = GetOrCreateModel(bId, "Continental GTC", "bentley-continental-gtc");
            PrepareGenerations(cgtc,
                ("I (2006–2011)", "bentley-cgtc-i", 2006, 2011),
                ("II (2011–2018)", "bentley-cgtc-ii", 2011, 2018),
                ("III (2019–)", "bentley-cgtc-iii", 2019, null));
            AddOrReplaceEngines(GetOrFixGeneration(cgtc, "I (2006–2011)", "bentley-cgtc-i", 2006, 2011), 400, [
                new EngineVersion { EngineName = "6.0 W12 560 KM", PowerHP = 560, PowerKW = 412, Displacement = 5998, FuelTypeId = ben,
                    TorqueNm = 650, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 5.1m, TopSpeedKmh = 312, FuelConsumptionCombined = 17.3m },
                new EngineVersion { EngineName = "6.0 W12 Speed 610 KM", PowerHP = 610, PowerKW = 449, Displacement = 5998, FuelTypeId = ben,
                    TorqueNm = 750, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 4.6m, TopSpeedKmh = 328, FuelConsumptionCombined = 17.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(cgtc, "II (2011–2018)", "bentley-cgtc-ii", 2011, 2018), 400, [
                new EngineVersion { EngineName = "6.0 W12 575 KM", PowerHP = 575, PowerKW = 423, Displacement = 5998, FuelTypeId = ben,
                    TorqueNm = 800, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 4.5m, TopSpeedKmh = 314, FuelConsumptionCombined = 15.6m },
                new EngineVersion { EngineName = "4.0 V8 507 KM", PowerHP = 507, PowerKW = 373, Displacement = 3993, FuelTypeId = ben,
                    TorqueNm = 660, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.9m, TopSpeedKmh = 303, FuelConsumptionCombined = 12.3m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(cgtc, "III (2019–)", "bentley-cgtc-iii", 2019, null), 400, [
                new EngineVersion { EngineName = "6.0 W12 635 KM", PowerHP = 635, PowerKW = 467, Displacement = 5950, FuelTypeId = ben,
                    TorqueNm = 900, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 3.8m, TopSpeedKmh = 333, FuelConsumptionCombined = 15.1m },
                new EngineVersion { EngineName = "4.0 V8 550 KM", PowerHP = 550, PowerKW = 404, Displacement = 3996, FuelTypeId = ben,
                    TorqueNm = 770, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.1m, TopSpeedKmh = 318, FuelConsumptionCombined = 12.6m },
            ]);
        }

        // ── ASTON MARTIN ────────────────────────────────────────────────────────
        {
            int bId = GetOrCreateBrand("Aston Martin", "aston-martin", "auta-osobowe");
            if (BrandNeedsModels(bId)) { seededModelBrandIds.Add(bId); }

            int db11 = GetOrCreateModel(bId, "DB11", "aston-martin-db11");
            AddOrReplaceEngines(GetOrFixGeneration(db11, "DB11 V8 (2016–)", "aston-db11-v8", 2016, null), 400, [
                new EngineVersion { EngineName = "4.0 V8 Bi-Turbo 510 KM", PowerHP = 510, PowerKW = 375, Displacement = 3982, FuelTypeId = ben,
                    TorqueNm = 675, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 4.0m, TopSpeedKmh = 301, FuelConsumptionCombined = 13.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(db11, "DB11 V12 (2016–)", "aston-db11-v12", 2016, null), 400, [
                new EngineVersion { EngineName = "5.2 V12 Bi-Turbo 600 KM", PowerHP = 600, PowerKW = 441, Displacement = 5204, FuelTypeId = ben,
                    TorqueNm = 700, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 3.7m, TopSpeedKmh = 322, FuelConsumptionCombined = 13.8m },
                new EngineVersion { EngineName = "5.2 V12 AMR 639 KM", PowerHP = 639, PowerKW = 470, Displacement = 5204, FuelTypeId = ben,
                    TorqueNm = 700, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 3.7m, TopSpeedKmh = 333, FuelConsumptionCombined = 14.4m },
            ]);

            int dbs = GetOrCreateModel(bId, "DBS Superleggera", "aston-martin-dbs");
            AddOrReplaceEngines(GetOrFixGeneration(dbs, "DBS (2018–)", "aston-dbs-2018", 2018, null), 400, [
                new EngineVersion { EngineName = "5.2 V12 Bi-Turbo 725 KM", PowerHP = 725, PowerKW = 533, Displacement = 5204, FuelTypeId = ben,
                    TorqueNm = 900, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 3.4m, TopSpeedKmh = 340, FuelConsumptionCombined = 14.9m },
            ]);

            int dbx = GetOrCreateModel(bId, "DBX", "aston-martin-dbx");
            AddOrReplaceEngines(GetOrFixGeneration(dbx, "DBX V8 (2020–)", "aston-dbx-v8", 2020, null), 400, [
                new EngineVersion { EngineName = "4.0 V8 Bi-Turbo 550 KM", PowerHP = 550, PowerKW = 404, Displacement = 3982, FuelTypeId = ben,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.5m, TopSpeedKmh = 291, FuelConsumptionCombined = 14.2m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(dbx, "DBX707 (2022–)", "aston-dbx707", 2022, null), 400, [
                new EngineVersion { EngineName = "4.0 V8 Bi-Turbo 707 KM", PowerHP = 707, PowerKW = 520, Displacement = 3982, FuelTypeId = ben,
                    TorqueNm = 900, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 3.3m, TopSpeedKmh = 310, FuelConsumptionCombined = 14.8m },
            ]);

            int vantage = GetOrCreateModel(bId, "Vantage", "aston-martin-vantage");
            AddOrReplaceEngines(GetOrFixGeneration(vantage, "Vantage II (2018–)", "aston-vantage-ii", 2018, null), 400, [
                new EngineVersion { EngineName = "4.0 V8 Bi-Turbo 510 KM", PowerHP = 510, PowerKW = 375, Displacement = 3982, FuelTypeId = ben,
                    TorqueNm = 685, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.7m, TopSpeedKmh = 314, FuelConsumptionCombined = 13.3m },
                new EngineVersion { EngineName = "4.0 V8 F1 Edition 535 KM", PowerHP = 535, PowerKW = 393, Displacement = 3982, FuelTypeId = ben,
                    TorqueNm = 685, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.6m, TopSpeedKmh = 320, FuelConsumptionCombined = 13.5m },
            ]);

            int db12 = GetOrCreateModel(bId, "DB12", "aston-martin-db12");
            AddOrReplaceEngines(GetOrFixGeneration(db12, "DB12 (2023–)", "aston-db12-2023", 2023, null), 400, [
                new EngineVersion { EngineName = "4.0 V8 Bi-Turbo 680 KM", PowerHP = 680, PowerKW = 500, Displacement = 3982, FuelTypeId = ben,
                    TorqueNm = 800, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.5m, TopSpeedKmh = 325, FuelConsumptionCombined = 13.5m },
            ]);

            int db7 = GetOrCreateModel(bId, "DB7", "aston-martin-db7");
            AddOrReplaceEngines(GetOrFixGeneration(db7, "I (1994–2003)", "aston-db7-i", 1994, 2003), 300, [
                new EngineVersion { EngineName = "3.2 I6 Supercharged 340 KM", PowerHP = 340, PowerKW = 250, Displacement = 3239, FuelTypeId = ben,
                    TorqueNm = 490, EuroNorm = "Euro 3", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 5.8m, TopSpeedKmh = 265, FuelConsumptionCombined = 16.5m },
                new EngineVersion { EngineName = "5.9 V12 Vantage 420 KM", PowerHP = 420, PowerKW = 309, Displacement = 5935, FuelTypeId = ben,
                    TorqueNm = 542, EuroNorm = "Euro 3", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 5.0m, TopSpeedKmh = 298, FuelConsumptionCombined = 18.5m },
            ]);

            int db9 = GetOrCreateModel(bId, "DB9", "aston-martin-db9");
            AddOrReplaceEngines(GetOrFixGeneration(db9, "I (2004–2016)", "aston-db9-i", 2004, 2016), 400, [
                new EngineVersion { EngineName = "5.9 V12 470 KM", PowerHP = 470, PowerKW = 346, Displacement = 5935, FuelTypeId = ben,
                    TorqueNm = 570, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 4.9m, TopSpeedKmh = 290, FuelConsumptionCombined = 16.9m },
                new EngineVersion { EngineName = "5.9 V12 GT 517 KM", PowerHP = 517, PowerKW = 380, Displacement = 5935, FuelTypeId = ben,
                    TorqueNm = 620, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 4.5m, TopSpeedKmh = 295, FuelConsumptionCombined = 15.8m },
            ]);

            // "DBS" here is the classic 2007–2012 DB9-based DBS and the later DBS Superleggera —
            // a separate model row from "DBS Superleggera" above (which the DB stored under its
            // own distinct model name), each with its own placeholder generation to fix.
            int dbsClassic = GetOrCreateModel(bId, "DBS", "aston-martin-dbs-classic");
            PrepareGenerations(dbsClassic,
                ("I (2007–2012)", "aston-dbs-classic-i", 2007, 2012),
                ("II (2018–2023)", "aston-dbs-classic-ii", 2018, 2023));
            AddOrReplaceEngines(GetOrFixGeneration(dbsClassic, "I (2007–2012)", "aston-dbs-classic-i", 2007, 2012), 400, [
                new EngineVersion { EngineName = "5.9 V12 517 KM", PowerHP = 517, PowerKW = 380, Displacement = 5935, FuelTypeId = ben,
                    TorqueNm = 570, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 4.3m, TopSpeedKmh = 307, FuelConsumptionCombined = 18.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(dbsClassic, "II (2018–2023)", "aston-dbs-classic-ii", 2018, 2023), 400, [
                new EngineVersion { EngineName = "5.2 V12 Bi-Turbo Superleggera 725 KM", PowerHP = 725, PowerKW = 533, Displacement = 5204, FuelTypeId = ben,
                    TorqueNm = 900, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 3.4m, TopSpeedKmh = 340, FuelConsumptionCombined = 14.9m },
            ]);

            int one77 = GetOrCreateModel(bId, "One-77", "aston-martin-one-77");
            AddOrReplaceEngines(GetOrFixGeneration(one77, "I (2010–2012)", "aston-one77-i", 2010, 2012), 700, [
                new EngineVersion { EngineName = "7.3 V12 750 KM", PowerHP = 750, PowerKW = 552, Displacement = 7312, FuelTypeId = ben,
                    TorqueNm = 750, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 3.7m, TopSpeedKmh = 354, FuelConsumptionCombined = 20.0m },
            ]);

            int rapide = GetOrCreateModel(bId, "Rapide", "aston-martin-rapide");
            AddOrReplaceEngines(GetOrFixGeneration(rapide, "I (2010–2020)", "aston-rapide-i", 2010, 2020), 400, [
                new EngineVersion { EngineName = "5.9 V12 477 KM", PowerHP = 477, PowerKW = 350, Displacement = 5935, FuelTypeId = ben,
                    TorqueNm = 600, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 5.1m, TopSpeedKmh = 296, FuelConsumptionCombined = 16.9m },
                new EngineVersion { EngineName = "6.0 V12 AMR 595 KM", PowerHP = 595, PowerKW = 438, Displacement = 5935, FuelTypeId = ben,
                    TorqueNm = 630, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 4.4m, TopSpeedKmh = 315, FuelConsumptionCombined = 17.2m },
            ]);

            int vanquish = GetOrCreateModel(bId, "Vanquish", "aston-martin-vanquish");
            PrepareGenerations(vanquish,
                ("I (2001–2007)", "aston-vanquish-i", 2001, 2007),
                ("II (2012–2018)", "aston-vanquish-ii", 2012, 2018));
            AddOrReplaceEngines(GetOrFixGeneration(vanquish, "I (2001–2007)", "aston-vanquish-i", 2001, 2007), 400, [
                new EngineVersion { EngineName = "5.9 V12 460 KM", PowerHP = 460, PowerKW = 338, Displacement = 5935, FuelTypeId = ben,
                    TorqueNm = 542, EuroNorm = "Euro 3", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 5.0m, TopSpeedKmh = 306, FuelConsumptionCombined = 18.9m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(vanquish, "II (2012–2018)", "aston-vanquish-ii", 2012, 2018), 400, [
                new EngineVersion { EngineName = "5.9 V12 573 KM", PowerHP = 573, PowerKW = 421, Displacement = 5935, FuelTypeId = ben,
                    TorqueNm = 630, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 4.1m, TopSpeedKmh = 295, FuelConsumptionCombined = 17.4m },
                new EngineVersion { EngineName = "5.9 V12 S 600 KM", PowerHP = 600, PowerKW = 441, Displacement = 5935, FuelTypeId = ben,
                    TorqueNm = 630, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 3.5m, TopSpeedKmh = 324, FuelConsumptionCombined = 17.9m },
            ]);

            int virage = GetOrCreateModel(bId, "Virage", "aston-martin-virage");
            AddOrReplaceEngines(GetOrFixGeneration(virage, "I (2011–2012)", "aston-virage-i", 2011, 2012), 400, [
                new EngineVersion { EngineName = "5.9 V12 490 KM", PowerHP = 490, PowerKW = 360, Displacement = 5935, FuelTypeId = ben,
                    TorqueNm = 570, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 4.6m, TopSpeedKmh = 300, FuelConsumptionCombined = 17.0m },
            ]);
        }

        // ── CADILLAC ────────────────────────────────────────────────────────────
        {
            int bId = GetOrCreateBrand("Cadillac", "cadillac", "auta-osobowe");
            if (BrandNeedsModels(bId)) { seededModelBrandIds.Add(bId); }

            int ats = GetOrCreateModel(bId, "ATS", "cadillac-ats");
            AddOrReplaceEngines(GetOrFixGeneration(ats, "I (2012–2019)", "cadillac-ats-i", 2012, 2019), 180, [
                new EngineVersion { EngineName = "2.0T 272 KM", PowerHP = 272, PowerKW = 200, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 5.7m, TopSpeedKmh = 249, FuelConsumptionCombined = 9.5m },
                new EngineVersion { EngineName = "3.6 V6 340 KM", PowerHP = 340, PowerKW = 250, Displacement = 3649, FuelTypeId = ben,
                    TorqueNm = 373, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.6m, TopSpeedKmh = 249, FuelConsumptionCombined = 11.2m },
                new EngineVersion { EngineName = "ATS-V 3.6 Twin-Turbo 464 KM", PowerHP = 464, PowerKW = 341, Displacement = 3649, FuelTypeId = ben,
                    TorqueNm = 603, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 3.9m, TopSpeedKmh = 305, FuelConsumptionCombined = 12.8m },
            ]);

            int ct4 = GetOrCreateModel(bId, "CT4", "cadillac-ct4");
            AddOrReplaceEngines(GetOrFixGeneration(ct4, "I (2019–)", "cadillac-ct4-i", 2019, null), 200, [
                new EngineVersion { EngineName = "2.0T 237 KM", PowerHP = 237, PowerKW = 174, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 6.6m, TopSpeedKmh = 209, FuelConsumptionCombined = 9.0m },
                new EngineVersion { EngineName = "CT4-V 325 KM", PowerHP = 325, PowerKW = 239, Displacement = 2661, FuelTypeId = ben,
                    TorqueNm = 475, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.0m, TopSpeedKmh = 240, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "CT4-V Blackwing 476 KM", PowerHP = 476, PowerKW = 350, Displacement = 3649, FuelTypeId = ben,
                    TorqueNm = 603, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 3.9m, TopSpeedKmh = 306, FuelConsumptionCombined = 13.2m },
            ]);

            int ct5 = GetOrCreateModel(bId, "CT5", "cadillac-ct5");
            AddOrReplaceEngines(GetOrFixGeneration(ct5, "I (2019–)", "cadillac-ct5-i", 2019, null), 200, [
                new EngineVersion { EngineName = "2.0T 237 KM", PowerHP = 237, PowerKW = 174, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 6.6m, TopSpeedKmh = 210, FuelConsumptionCombined = 9.2m },
                new EngineVersion { EngineName = "3.0TT 335 KM", PowerHP = 335, PowerKW = 246, Displacement = 2996, FuelTypeId = ben,
                    TorqueNm = 542, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.0m, TopSpeedKmh = 240, FuelConsumptionCombined = 10.8m },
                new EngineVersion { EngineName = "CT5-V Blackwing 668 KM", PowerHP = 668, PowerKW = 491, Displacement = 6162, FuelTypeId = ben,
                    TorqueNm = 893, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.6m, TopSpeedKmh = 320, FuelConsumptionCombined = 15.3m },
            ]);

            int cts = GetOrCreateModel(bId, "CTS", "cadillac-cts");
            PrepareGenerations(cts,
                ("I (2002–2007)", "cadillac-cts-i", 2002, 2007),
                ("II (2007–2013)", "cadillac-cts-ii", 2007, 2013),
                ("III (2013–2019)", "cadillac-cts-iii", 2013, 2019));
            AddOrReplaceEngines(GetOrFixGeneration(cts, "I (2002–2007)", "cadillac-cts-i", 2002, 2007), 150, [
                new EngineVersion { EngineName = "3.2 V6 220 KM", PowerHP = 220, PowerKW = 162, Displacement = 3172, FuelTypeId = ben,
                    TorqueNm = 305, EuroNorm = "Euro 3", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 8.4m, TopSpeedKmh = 220, FuelConsumptionCombined = 12.5m },
                new EngineVersion { EngineName = "3.6 V6 260 KM", PowerHP = 260, PowerKW = 191, Displacement = 3564, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 6.9m, TopSpeedKmh = 240, FuelConsumptionCombined = 12.9m },
                new EngineVersion { EngineName = "CTS-V 5.7 V8 400 KM", PowerHP = 400, PowerKW = 294, Displacement = 5665, FuelTypeId = ben,
                    TorqueNm = 529, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 5.0m, TopSpeedKmh = 262, FuelConsumptionCombined = 15.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(cts, "II (2007–2013)", "cadillac-cts-ii", 2007, 2013), 200, [
                new EngineVersion { EngineName = "3.0 V6 273 KM", PowerHP = 273, PowerKW = 201, Displacement = 2967, FuelTypeId = ben,
                    TorqueNm = 320, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 7.0m, TopSpeedKmh = 230, FuelConsumptionCombined = 11.5m },
                new EngineVersion { EngineName = "3.6 V6 311 KM", PowerHP = 311, PowerKW = 229, Displacement = 3564, FuelTypeId = ben,
                    TorqueNm = 373, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.0m, TopSpeedKmh = 248, FuelConsumptionCombined = 12.3m },
                new EngineVersion { EngineName = "CTS-V 6.2 Supercharged 556 KM", PowerHP = 556, PowerKW = 409, Displacement = 6162, FuelTypeId = ben,
                    TorqueNm = 747, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.9m, TopSpeedKmh = 291, FuelConsumptionCombined = 16.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(cts, "III (2013–2019)", "cadillac-cts-iii", 2013, 2019), 200, [
                new EngineVersion { EngineName = "2.0T 276 KM", PowerHP = 276, PowerKW = 203, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 5.9m, TopSpeedKmh = 249, FuelConsumptionCombined = 9.4m },
                new EngineVersion { EngineName = "3.6 V6 335 KM", PowerHP = 335, PowerKW = 246, Displacement = 3649, FuelTypeId = ben,
                    TorqueNm = 373, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.6m, TopSpeedKmh = 249, FuelConsumptionCombined = 11.6m },
                new EngineVersion { EngineName = "CTS-V 6.2 Supercharged 640 KM", PowerHP = 640, PowerKW = 471, Displacement = 6162, FuelTypeId = ben,
                    TorqueNm = 855, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.7m, TopSpeedKmh = 320, FuelConsumptionCombined = 16.9m },
            ]);

            int escalade = GetOrCreateModel(bId, "Escalade", "cadillac-escalade");
            PrepareGenerations(escalade,
                ("I (1998–2001)", "cadillac-escalade-i", 1998, 2001),
                ("II (2001–2006)", "cadillac-escalade-ii", 2001, 2006),
                ("III (2006–2014)", "cadillac-escalade-iii", 2006, 2014),
                ("IV (2014–2020)", "cadillac-escalade-iv", 2014, 2020),
                ("V (2020–)", "cadillac-escalade-v", 2020, null));
            AddOrReplaceEngines(GetOrFixGeneration(escalade, "I (1998–2001)", "cadillac-escalade-i", 1998, 2001), 200, [
                new EngineVersion { EngineName = "5.7 V8 260 KM", PowerHP = 260, PowerKW = 191, Displacement = 5665, FuelTypeId = ben,
                    TorqueNm = 447, EuroNorm = "Euro 2", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 8.9m, TopSpeedKmh = 180, FuelConsumptionCombined = 17.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(escalade, "II (2001–2006)", "cadillac-escalade-ii", 2001, 2006), 300, [
                new EngineVersion { EngineName = "6.0 V8 345 KM", PowerHP = 345, PowerKW = 254, Displacement = 5967, FuelTypeId = ben,
                    TorqueNm = 495, EuroNorm = "Euro 3", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 7.3m, TopSpeedKmh = 180, FuelConsumptionCombined = 18.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(escalade, "III (2006–2014)", "cadillac-escalade-iii", 2006, 2014), 380, [
                new EngineVersion { EngineName = "6.2 V8 409 KM", PowerHP = 409, PowerKW = 301, Displacement = 6162, FuelTypeId = ben,
                    TorqueNm = 566, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 6.0m, TopSpeedKmh = 180, FuelConsumptionCombined = 17.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(escalade, "IV (2014–2020)", "cadillac-escalade-iv", 2014, 2020), 390, [
                new EngineVersion { EngineName = "6.2 V8 420 KM", PowerHP = 420, PowerKW = 309, Displacement = 6162, FuelTypeId = ben,
                    TorqueNm = 623, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 6.0m, TopSpeedKmh = 180, FuelConsumptionCombined = 15.9m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(escalade, "V (2020–)", "cadillac-escalade-v", 2020, null), 250, [
                new EngineVersion { EngineName = "6.2 V8 426 KM", PowerHP = 426, PowerKW = 313, Displacement = 6162, FuelTypeId = ben,
                    TorqueNm = 623, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 6.0m, TopSpeedKmh = 210, FuelConsumptionCombined = 15.0m },
                new EngineVersion { EngineName = "3.0 Duramax Diesel 277 KM", PowerHP = 277, PowerKW = 204, Displacement = 2993, FuelTypeId = die,
                    TorqueNm = 623, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 7.8m, TopSpeedKmh = 190, FuelConsumptionCombined = 9.4m },
            ]);

            int srx = GetOrCreateModel(bId, "SRX", "cadillac-srx");
            PrepareGenerations(srx,
                ("I (2003–2009)", "cadillac-srx-i", 2003, 2009),
                ("II (2009–2016)", "cadillac-srx-ii", 2009, 2016));
            AddOrReplaceEngines(GetOrFixGeneration(srx, "I (2003–2009)", "cadillac-srx-i", 2003, 2009), 200, [
                new EngineVersion { EngineName = "3.6 V6 260 KM", PowerHP = 260, PowerKW = 191, Displacement = 3564, FuelTypeId = ben,
                    TorqueNm = 346, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 8.0m, TopSpeedKmh = 200, FuelConsumptionCombined = 13.5m },
                new EngineVersion { EngineName = "4.6 V8 320 KM", PowerHP = 320, PowerKW = 235, Displacement = 4565, FuelTypeId = ben,
                    TorqueNm = 420, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 7.0m, TopSpeedKmh = 210, FuelConsumptionCombined = 15.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(srx, "II (2009–2016)", "cadillac-srx-ii", 2009, 2016), 200, [
                new EngineVersion { EngineName = "3.0 V6 265 KM", PowerHP = 265, PowerKW = 195, Displacement = 2996, FuelTypeId = ben,
                    TorqueNm = 305, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 7.8m, TopSpeedKmh = 200, FuelConsumptionCombined = 12.2m },
                new EngineVersion { EngineName = "2.8T 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 2792, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.5m, TopSpeedKmh = 220, FuelConsumptionCombined = 13.0m },
            ]);

            int xt4 = GetOrCreateModel(bId, "XT4", "cadillac-xt4");
            AddOrReplaceEngines(GetOrFixGeneration(xt4, "I (2018–)", "cadillac-xt4-i", 2018, null), 180, [
                new EngineVersion { EngineName = "2.0T 237 KM", PowerHP = 237, PowerKW = 174, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.4m, TopSpeedKmh = 209, FuelConsumptionCombined = 9.7m },
            ]);

            int xt5 = GetOrCreateModel(bId, "XT5", "cadillac-xt5");
            AddOrReplaceEngines(GetOrFixGeneration(xt5, "I (2016–)", "cadillac-xt5-i", 2016, null), 200, [
                new EngineVersion { EngineName = "2.0T 237 KM", PowerHP = 237, PowerKW = 174, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 353, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.9m, TopSpeedKmh = 200, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "3.6 V6 314 KM", PowerHP = 314, PowerKW = 231, Displacement = 3649, FuelTypeId = ben,
                    TorqueNm = 370, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.7m, TopSpeedKmh = 210, FuelConsumptionCombined = 12.1m },
            ]);

            int xt6 = GetOrCreateModel(bId, "XT6", "cadillac-xt6");
            AddOrReplaceEngines(GetOrFixGeneration(xt6, "I (2019–)", "cadillac-xt6-i", 2019, null), 200, [
                new EngineVersion { EngineName = "3.6 V6 314 KM", PowerHP = 314, PowerKW = 231, Displacement = 3649, FuelTypeId = ben,
                    TorqueNm = 370, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 7.3m, TopSpeedKmh = 210, FuelConsumptionCombined = 12.3m },
            ]);
        }

        // ── McLAREN ─────────────────────────────────────────────────────────────
        {
            int bId = GetOrCreateBrand("McLaren", "mclaren", "auta-osobowe");
            if (BrandNeedsModels(bId)) { seededModelBrandIds.Add(bId); }

            int m650 = GetOrCreateModel(bId, "650S", "mclaren-650s");
            AddOrReplaceEngines(GetOrFixGeneration(m650, "650S (2014–2017)", "mclaren-650s-2014", 2014, 2017), 500, [
                new EngineVersion { EngineName = "3.8 V8 Bi-Turbo 650 KM", PowerHP = 650, PowerKW = 478, Displacement = 3799, FuelTypeId = ben,
                    TorqueNm = 678, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.0m, TopSpeedKmh = 329, FuelConsumptionCombined = 12.8m },
            ]);

            int m570 = GetOrCreateModel(bId, "570S", "mclaren-570s");
            AddOrReplaceEngines(GetOrFixGeneration(m570, "Sports Series (2015–2022)", "mclaren-570s-2015", 2015, 2022), 500, [
                new EngineVersion { EngineName = "3.8 V8 Bi-Turbo 570 KM", PowerHP = 570, PowerKW = 419, Displacement = 3799, FuelTypeId = ben,
                    TorqueNm = 600, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.2m, TopSpeedKmh = 328, FuelConsumptionCombined = 13.1m },
                new EngineVersion { EngineName = "3.8 V8 Bi-Turbo 540C 540 KM", PowerHP = 540, PowerKW = 397, Displacement = 3799, FuelTypeId = ben,
                    TorqueNm = 570, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.5m, TopSpeedKmh = 320, FuelConsumptionCombined = 13.0m },
            ]);

            int m720 = GetOrCreateModel(bId, "720S", "mclaren-720s");
            AddOrReplaceEngines(GetOrFixGeneration(m720, "720S (2017–2022)", "mclaren-720s-2017", 2017, 2022), 500, [
                new EngineVersion { EngineName = "4.0 V8 Bi-Turbo 720 KM", PowerHP = 720, PowerKW = 529, Displacement = 3994, FuelTypeId = ben,
                    TorqueNm = 770, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 2.9m, TopSpeedKmh = 341, FuelConsumptionCombined = 13.4m },
                new EngineVersion { EngineName = "4.0 V8 765LT 765 KM", PowerHP = 765, PowerKW = 562, Displacement = 3994, FuelTypeId = ben,
                    TorqueNm = 800, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 2.8m, TopSpeedKmh = 330, FuelConsumptionCombined = 14.0m },
            ]);

            int artura = GetOrCreateModel(bId, "Artura", "mclaren-artura");
            AddOrReplaceEngines(GetOrFixGeneration(artura, "Artura (2021–)", "mclaren-artura-2021", 2021, null), 500, [
                new EngineVersion { EngineName = "3.0 V6 Bi-Turbo + Hybrid 700 KM", PowerHP = 700, PowerKW = 515, Displacement = 2993, FuelTypeId = phev,
                    TorqueNm = 720, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 3.0m, TopSpeedKmh = 330, FuelConsumptionCombined = 6.5m },
            ]);

            int mgt = GetOrCreateModel(bId, "GT", "mclaren-gt");
            AddOrReplaceEngines(GetOrFixGeneration(mgt, "GT (2019–)", "mclaren-gt-2019", 2019, null), 500, [
                new EngineVersion { EngineName = "4.0 V8 Bi-Turbo 620 KM", PowerHP = 620, PowerKW = 456, Displacement = 3994, FuelTypeId = ben,
                    TorqueNm = 630, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.2m, TopSpeedKmh = 326, FuelConsumptionCombined = 13.1m },
            ]);

            int m600lt = GetOrCreateModel(bId, "600LT", "mclaren-600lt");
            AddOrReplaceEngines(GetOrFixGeneration(m600lt, "600LT (2018–2019)", "mclaren-600lt-2018", 2018, 2019), 550, [
                new EngineVersion { EngineName = "3.8 V8 Bi-Turbo 600 KM", PowerHP = 600, PowerKW = 441, Displacement = 3799, FuelTypeId = ben,
                    TorqueNm = 620, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 2.9m, TopSpeedKmh = 328, FuelConsumptionCombined = 13.5m },
            ]);

            int m675lt = GetOrCreateModel(bId, "675LT", "mclaren-675lt");
            AddOrReplaceEngines(GetOrFixGeneration(m675lt, "675LT (2015–2017)", "mclaren-675lt-2015", 2015, 2017), 600, [
                new EngineVersion { EngineName = "3.8 V8 Bi-Turbo 675 KM", PowerHP = 675, PowerKW = 496, Displacement = 3799, FuelTypeId = ben,
                    TorqueNm = 700, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 2.9m, TopSpeedKmh = 330, FuelConsumptionCombined = 13.4m },
            ]);

            int p1 = GetOrCreateModel(bId, "P1", "mclaren-p1");
            AddOrReplaceEngines(GetOrFixGeneration(p1, "P1 (2013–2015)", "mclaren-p1-2013", 2013, 2015), 700, [
                new EngineVersion { EngineName = "3.8 V8 Bi-Turbo + electric 916 KM", PowerHP = 916, PowerKW = 674, Displacement = 3799, FuelTypeId = phev,
                    TorqueNm = 900, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 2.8m, TopSpeedKmh = 350, FuelConsumptionCombined = 8.3m },
            ]);

            int senna = GetOrCreateModel(bId, "Senna", "mclaren-senna");
            AddOrReplaceEngines(GetOrFixGeneration(senna, "Senna (2018–2019)", "mclaren-senna-2018", 2018, 2019), 700, [
                new EngineVersion { EngineName = "4.0 V8 Bi-Turbo 800 KM", PowerHP = 800, PowerKW = 588, Displacement = 3994, FuelTypeId = ben,
                    TorqueNm = 800, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 2.8m, TopSpeedKmh = 336, FuelConsumptionCombined = 14.5m },
            ]);
        }

        // ── HONDA (motorcycles) ─────────────────────────────────────────────────
        {
            int bId = GetOrCreateBrand("Honda", "honda", "auta-osobowe", "motocykle");
            // Honda cars already seeded by ModelSeeder; only add moto models if needed
            int cbr = GetOrCreateModel(bId, "CBR1000RR-R Fireblade", "honda-cbr1000rr-r");
            AddEngines(GetOrCreateGeneration(cbr, "SC82 (2020–)", "honda-cbr1000rr-r-sc82", 2020, null), [
                new EngineVersion { EngineName = "999 cc I4 217 KM", PowerHP = 217, PowerKW = 160, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 113, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 3.0m, TopSpeedKmh = 299, FuelConsumptionCombined = 6.8m },
                new EngineVersion { EngineName = "999 cc I4 SP 217 KM", PowerHP = 217, PowerKW = 160, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 113, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 3.0m, TopSpeedKmh = 299, FuelConsumptionCombined = 6.8m },
            ]);

            int africaTwin = GetOrCreateModel(bId, "Africa Twin CRF1100L", "honda-africa-twin-crf1100");
            AddEngines(GetOrCreateGeneration(africaTwin, "CRF1100L (2020–)", "honda-africa-twin-2020", 2020, null), [
                new EngineVersion { EngineName = "1084 cc Parallel Twin 101 KM", PowerHP = 101, PowerKW = 74, Displacement = 1084, FuelTypeId = ben,
                    TorqueNm = 105, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 4.5m, TopSpeedKmh = 220, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "1084 cc Parallel Twin DCT 101 KM", PowerHP = 101, PowerKW = 74, Displacement = 1084, FuelTypeId = ben,
                    TorqueNm = 105, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 4.5m, TopSpeedKmh = 220, FuelConsumptionCombined = 5.6m },
            ]);

            int goldwing = GetOrCreateModel(bId, "Gold Wing GL1800", "honda-gold-wing-gl1800");
            AddEngines(GetOrCreateGeneration(goldwing, "GL1800 (2018–)", "honda-gold-wing-2018", 2018, null), [
                new EngineVersion { EngineName = "1833 cc Flat-6 126 KM", PowerHP = 126, PowerKW = 93, Displacement = 1833, FuelTypeId = ben,
                    TorqueNm = 170, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 5.5m, TopSpeedKmh = 200, FuelConsumptionCombined = 6.1m },
                new EngineVersion { EngineName = "1833 cc Flat-6 Tour DCT 126 KM", PowerHP = 126, PowerKW = 93, Displacement = 1833, FuelTypeId = ben,
                    TorqueNm = 170, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 5.5m, TopSpeedKmh = 200, FuelConsumptionCombined = 6.3m },
            ]);

            int cb650r = GetOrCreateModel(bId, "CB650R", "honda-cb650r");
            AddEngines(GetOrCreateGeneration(cb650r, "RH02 (2019–)", "honda-cb650r-rh02", 2019, null), [
                new EngineVersion { EngineName = "649 cc I4 94 KM", PowerHP = 94, PowerKW = 69, Displacement = 649, FuelTypeId = ben,
                    TorqueNm = 63, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 4.2m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.0m },
            ]);

            int nc750x = GetOrCreateModel(bId, "NC750X", "honda-nc750x");
            AddEngines(GetOrCreateGeneration(nc750x, "NC750X (2021–)", "honda-nc750x-2021", 2021, null), [
                new EngineVersion { EngineName = "745 cc Parallel Twin 58 KM", PowerHP = 58, PowerKW = 43, Displacement = 745, FuelTypeId = ben,
                    TorqueNm = 69, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, TopSpeedKmh = 185, FuelConsumptionCombined = 4.2m },
                new EngineVersion { EngineName = "745 cc Parallel Twin DCT 58 KM", PowerHP = 58, PowerKW = 43, Displacement = 745, FuelTypeId = ben,
                    TorqueNm = 69, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 2, TopSpeedKmh = 185, FuelConsumptionCombined = 4.4m },
            ]);
        }

        // ── CFMoto (motorcycles) ─────────────────────────────────────────────────
        {
            int bId = GetOrCreateBrand("CFMoto", "cfmoto", "motocykle");
            if (BrandNeedsModels(bId)) { seededModelBrandIds.Add(bId); }

            int m800 = GetOrCreateModel(bId, "800MT", "cfmoto-800mt");
            AddEngines(GetOrCreateGeneration(m800, "800MT (2022–)", "cfmoto-800mt-2022", 2022, null), [
                new EngineVersion { EngineName = "799 cc Parallel Twin 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 799, FuelTypeId = ben,
                    TorqueNm = 80, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, TopSpeedKmh = 210, FuelConsumptionCombined = 4.8m },
            ]);

            int m700 = GetOrCreateModel(bId, "700CL-X", "cfmoto-700clx");
            AddEngines(GetOrCreateGeneration(m700, "700CL-X (2021–)", "cfmoto-700clx-2021", 2021, null), [
                new EngineVersion { EngineName = "693 cc Parallel Twin 70 KM", PowerHP = 70, PowerKW = 51, Displacement = 693, FuelTypeId = ben,
                    TorqueNm = 68, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, TopSpeedKmh = 190, FuelConsumptionCombined = 4.5m },
            ]);

            int m450 = GetOrCreateModel(bId, "450SR", "cfmoto-450sr");
            AddEngines(GetOrCreateGeneration(m450, "450SR (2022–)", "cfmoto-450sr-2022", 2022, null), [
                new EngineVersion { EngineName = "449 cc Parallel Twin 51 KM", PowerHP = 51, PowerKW = 38, Displacement = 449, FuelTypeId = ben,
                    TorqueNm = 43, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, TopSpeedKmh = 175, FuelConsumptionCombined = 4.0m },
            ]);

            int m650mt = GetOrCreateModel(bId, "650MT", "cfmoto-650mt");
            AddEngines(GetOrCreateGeneration(m650mt, "650MT (2021–)", "cfmoto-650mt-2021", 2021, null), [
                new EngineVersion { EngineName = "649 cc Parallel Twin 71 KM", PowerHP = 71, PowerKW = 52, Displacement = 649, FuelTypeId = ben,
                    TorqueNm = 62, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, TopSpeedKmh = 190, FuelConsumptionCombined = 4.4m },
            ]);

            int m300 = GetOrCreateModel(bId, "300NK", "cfmoto-300nk");
            AddEngines(GetOrCreateGeneration(m300, "300NK (2020–)", "cfmoto-300nk-2020", 2020, null), [
                new EngineVersion { EngineName = "292 cc Single 29 KM", PowerHP = 29, PowerKW = 21, Displacement = 292, FuelTypeId = ben,
                    TorqueNm = 26, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 1, TopSpeedKmh = 145, FuelConsumptionCombined = 3.1m },
            ]);
        }

        // ── BENELLI (motorcycles) ────────────────────────────────────────────────
        {
            int bId = GetOrCreateBrand("Benelli", "benelli", "motocykle");
            if (BrandNeedsModels(bId)) { seededModelBrandIds.Add(bId); }

            int trk502 = GetOrCreateModel(bId, "TRK 502", "benelli-trk502");
            AddEngines(GetOrCreateGeneration(trk502, "TRK 502 (2017–)", "benelli-trk502-2017", 2017, null), [
                new EngineVersion { EngineName = "499 cc Parallel Twin 47.6 KM", PowerHP = 47, PowerKW = 35, Displacement = 499, FuelTypeId = ben,
                    TorqueNm = 46, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, TopSpeedKmh = 175, FuelConsumptionCombined = 4.2m },
            ]);

            int trk502x = GetOrCreateModel(bId, "TRK 502X", "benelli-trk502x");
            AddEngines(GetOrCreateGeneration(trk502x, "TRK 502X (2019–)", "benelli-trk502x-2019", 2019, null), [
                new EngineVersion { EngineName = "499 cc Parallel Twin 47.6 KM", PowerHP = 47, PowerKW = 35, Displacement = 499, FuelTypeId = ben,
                    TorqueNm = 46, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, TopSpeedKmh = 170, FuelConsumptionCombined = 4.3m },
            ]);

            int leo500 = GetOrCreateModel(bId, "Leoncino 500", "benelli-leoncino500");
            AddEngines(GetOrCreateGeneration(leo500, "Leoncino 500 (2018–)", "benelli-leoncino500-2018", 2018, null), [
                new EngineVersion { EngineName = "499 cc Parallel Twin 47.6 KM", PowerHP = 47, PowerKW = 35, Displacement = 499, FuelTypeId = ben,
                    TorqueNm = 45, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, TopSpeedKmh = 165, FuelConsumptionCombined = 4.1m },
            ]);

            int bn600 = GetOrCreateModel(bId, "302S", "benelli-302s");
            AddEngines(GetOrCreateGeneration(bn600, "302S (2017–)", "benelli-302s-2017", 2017, null), [
                new EngineVersion { EngineName = "300 cc Parallel Twin 38.3 KM", PowerHP = 38, PowerKW = 28, Displacement = 300, FuelTypeId = ben,
                    TorqueNm = 27, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, TopSpeedKmh = 160, FuelConsumptionCombined = 3.6m },
            ]);
        }

        // ── KAWASAKI (additional models) ─────────────────────────────────────────
        {
            int bId = GetOrCreateBrand("Kawasaki", "kawasaki", "motocykle");

            int z900 = GetOrCreateModel(bId, "Z900", "kawasaki-z900");
            AddEngines(GetOrCreateGeneration(z900, "ZR900 (2017–)", "kawasaki-z900-zr900", 2017, null), [
                new EngineVersion { EngineName = "948 cc I4 125 KM", PowerHP = 125, PowerKW = 92, Displacement = 948, FuelTypeId = ben,
                    TorqueNm = 99, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 3.5m, TopSpeedKmh = 248, FuelConsumptionCombined = 5.8m },
                new EngineVersion { EngineName = "948 cc I4 RS 125 KM", PowerHP = 125, PowerKW = 92, Displacement = 948, FuelTypeId = ben,
                    TorqueNm = 99, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 3.5m, TopSpeedKmh = 248, FuelConsumptionCombined = 5.8m },
            ]);

            int ninja400 = GetOrCreateModel(bId, "Ninja 400", "kawasaki-ninja400");
            AddEngines(GetOrCreateGeneration(ninja400, "EX400G (2018–)", "kawasaki-ninja400-ex400g", 2018, null), [
                new EngineVersion { EngineName = "399 cc Parallel Twin 45 KM", PowerHP = 45, PowerKW = 33, Displacement = 399, FuelTypeId = ben,
                    TorqueNm = 38, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, TopSpeedKmh = 180, FuelConsumptionCombined = 4.4m },
            ]);

            int versys1000 = GetOrCreateModel(bId, "Versys 1000", "kawasaki-versys1000");
            AddEngines(GetOrCreateGeneration(versys1000, "Versys 1000 (2019–)", "kawasaki-versys1000-2019", 2019, null), [
                new EngineVersion { EngineName = "1043 cc I4 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1043, FuelTypeId = ben,
                    TorqueNm = 102, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, TopSpeedKmh = 220, FuelConsumptionCombined = 5.9m },
            ]);

            int zx10r = GetOrCreateModel(bId, "ZX-10R", "kawasaki-zx10r");
            AddEngines(GetOrCreateGeneration(zx10r, "2021– (ZX1002L)", "kawasaki-zx10r-2021", 2021, null), [
                new EngineVersion { EngineName = "998 cc I4 203 KM", PowerHP = 203, PowerKW = 149, Displacement = 998, FuelTypeId = ben,
                    TorqueNm = 115, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 3.0m, TopSpeedKmh = 299, FuelConsumptionCombined = 6.9m },
            ]);
        }

        // ── TRIUMPH (additional models) ──────────────────────────────────────────
        {
            int bId = GetOrCreateBrand("Triumph", "triumph", "motocykle");

            int tiger900 = GetOrCreateModel(bId, "Tiger 900", "triumph-tiger900");
            AddEngines(GetOrCreateGeneration(tiger900, "2020–", "triumph-tiger900-2020", 2020, null), [
                new EngineVersion { EngineName = "888 cc Triple 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 888, FuelTypeId = ben,
                    TorqueNm = 87, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, Acceleration0100 = 4.5m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.2m },
            ]);

            int tiger1200 = GetOrCreateModel(bId, "Tiger 1200", "triumph-tiger1200");
            AddEngines(GetOrCreateGeneration(tiger1200, "Tiger 1200 (2022–)", "triumph-tiger1200-2022", 2022, null), [
                new EngineVersion { EngineName = "1160 cc Triple 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1160, FuelTypeId = ben,
                    TorqueNm = 130, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, Acceleration0100 = 4.0m, TopSpeedKmh = 220, FuelConsumptionCombined = 5.8m },
            ]);

            int speedTriple = GetOrCreateModel(bId, "Speed Triple 1200 RS", "triumph-speed-triple-1200rs");
            AddEngines(GetOrCreateGeneration(speedTriple, "Speed Triple 1200 RS (2021–)", "triumph-st1200rs-2021", 2021, null), [
                new EngineVersion { EngineName = "1160 cc Triple 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 1160, FuelTypeId = ben,
                    TorqueNm = 125, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, Acceleration0100 = 3.3m, TopSpeedKmh = 260, FuelConsumptionCombined = 6.5m },
            ]);

            int rocket3 = GetOrCreateModel(bId, "Rocket 3", "triumph-rocket3");
            AddEngines(GetOrCreateGeneration(rocket3, "Rocket 3 (2020–)", "triumph-rocket3-2020", 2020, null), [
                new EngineVersion { EngineName = "2458 cc Triple 167 KM", PowerHP = 167, PowerKW = 123, Displacement = 2458, FuelTypeId = ben,
                    TorqueNm = 221, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, TopSpeedKmh = 230, FuelConsumptionCombined = 8.0m },
            ]);

            int trident = GetOrCreateModel(bId, "Trident 660", "triumph-trident660");
            AddEngines(GetOrCreateGeneration(trident, "Trident 660 (2021–)", "triumph-trident660-2021", 2021, null), [
                new EngineVersion { EngineName = "660 cc Triple 81 KM", PowerHP = 81, PowerKW = 60, Displacement = 660, FuelTypeId = ben,
                    TorqueNm = 64, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, TopSpeedKmh = 195, FuelConsumptionCombined = 4.8m },
            ]);

            int bonneville = GetOrCreateModel(bId, "Bonneville T120", "triumph-bonneville-t120");
            AddEngines(GetOrCreateGeneration(bonneville, "2016–", "triumph-bonnie-t120-2016", 2016, null), [
                new EngineVersion { EngineName = "1200 cc Parallel Twin 80 KM", PowerHP = 80, PowerKW = 59, Displacement = 1200, FuelTypeId = ben,
                    TorqueNm = 105, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, TopSpeedKmh = 195, FuelConsumptionCombined = 5.1m },
            ]);
        }

        // ── VOLVO TRUCKS ─────────────────────────────────────────────────────────
        {
            int bId = GetOrCreateBrand("Volvo Trucks", "volvo-trucks", "ciezarowe");
            if (BrandNeedsModels(bId)) { seededModelBrandIds.Add(bId); }

            int fh16 = GetOrCreateModel(bId, "FH16", "volvo-fh16");
            AddEngines(GetOrCreateGeneration(fh16, "FH16 Gen.4 (2012–2021)", "volvo-fh16-gen4", 2012, 2021), [
                new EngineVersion { EngineName = "D16G 750 KM", PowerHP = 750, PowerKW = 552, Displacement = 16119, FuelTypeId = die,
                    TorqueNm = 3550, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "D16G 700 KM", PowerHP = 700, PowerKW = 515, Displacement = 16119, FuelTypeId = die,
                    TorqueNm = 3350, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "D16G 650 KM", PowerHP = 650, PowerKW = 478, Displacement = 16119, FuelTypeId = die,
                    TorqueNm = 3150, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
            ]);

            int fh = GetOrCreateModel(bId, "FH", "volvo-fh");
            AddEngines(GetOrCreateGeneration(fh, "FH Gen.4 (2012–2021)", "volvo-fh-gen4", 2012, 2021), [
                new EngineVersion { EngineName = "D13K 540 KM", PowerHP = 540, PowerKW = 397, Displacement = 12777, FuelTypeId = die,
                    TorqueNm = 2700, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "D13K 500 KM", PowerHP = 500, PowerKW = 368, Displacement = 12777, FuelTypeId = die,
                    TorqueNm = 2500, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "D13K 460 KM", PowerHP = 460, PowerKW = 338, Displacement = 12777, FuelTypeId = die,
                    TorqueNm = 2300, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "D13K 420 KM", PowerHP = 420, PowerKW = 309, Displacement = 12777, FuelTypeId = die,
                    TorqueNm = 2100, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
            ]);
            AddEngines(GetOrCreateGeneration(fh, "FH Gen.5 (2021–)", "volvo-fh-gen5", 2021, null), [
                new EngineVersion { EngineName = "D13TC 500 KM", PowerHP = 500, PowerKW = 368, Displacement = 12777, FuelTypeId = die,
                    TorqueNm = 2600, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "D13TC 460 KM", PowerHP = 460, PowerKW = 338, Displacement = 12777, FuelTypeId = die,
                    TorqueNm = 2400, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "FH Electric 490 KM", PowerHP = 490, PowerKW = 360, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 2400, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
            ]);

            int fm = GetOrCreateModel(bId, "FM", "volvo-fm");
            AddEngines(GetOrCreateGeneration(fm, "FM (2020–)", "volvo-fm-2020", 2020, null), [
                new EngineVersion { EngineName = "D13K 460 KM", PowerHP = 460, PowerKW = 338, Displacement = 12777, FuelTypeId = die,
                    TorqueNm = 2300, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "D13K 420 KM", PowerHP = 420, PowerKW = 309, Displacement = 12777, FuelTypeId = die,
                    TorqueNm = 2100, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "D11K 460 KM", PowerHP = 460, PowerKW = 338, Displacement = 10837, FuelTypeId = die,
                    TorqueNm = 2300, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "D11K 370 KM", PowerHP = 370, PowerKW = 272, Displacement = 10837, FuelTypeId = die,
                    TorqueNm = 1850, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
            ]);

            int fmx = GetOrCreateModel(bId, "FMX", "volvo-fmx");
            AddEngines(GetOrCreateGeneration(fmx, "FMX (2020–)", "volvo-fmx-2020", 2020, null), [
                new EngineVersion { EngineName = "D13K 500 KM", PowerHP = 500, PowerKW = 368, Displacement = 12777, FuelTypeId = die,
                    TorqueNm = 2500, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD" },
                new EngineVersion { EngineName = "D13K 460 KM", PowerHP = 460, PowerKW = 338, Displacement = 12777, FuelTypeId = die,
                    TorqueNm = 2300, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD" },
            ]);
        }

        // ── MAN TRUCK (additional models) ────────────────────────────────────────
        {
            int bId = GetOrCreateBrand("MAN", "man", "ciezarowe");

            int tgx = GetOrCreateModel(bId, "TGX", "man-tgx");
            AddEngines(GetOrCreateGeneration(tgx, "NEO (2020–)", "man-tgx-neo", 2020, null), [
                new EngineVersion { EngineName = "D3876 640 KM", PowerHP = 640, PowerKW = 471, Displacement = 15247, FuelTypeId = die,
                    TorqueNm = 3000, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "D3876 600 KM", PowerHP = 600, PowerKW = 441, Displacement = 15247, FuelTypeId = die,
                    TorqueNm = 2800, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "D2676 520 KM", PowerHP = 520, PowerKW = 383, Displacement = 12419, FuelTypeId = die,
                    TorqueNm = 2600, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "D2676 480 KM", PowerHP = 480, PowerKW = 353, Displacement = 12419, FuelTypeId = die,
                    TorqueNm = 2400, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
            ]);

            int tgs = GetOrCreateModel(bId, "TGS", "man-tgs");
            AddEngines(GetOrCreateGeneration(tgs, "II (2020–)", "man-tgs-ii", 2020, null), [
                new EngineVersion { EngineName = "D2676 480 KM", PowerHP = 480, PowerKW = 353, Displacement = 12419, FuelTypeId = die,
                    TorqueNm = 2400, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "D2676 440 KM", PowerHP = 440, PowerKW = 324, Displacement = 12419, FuelTypeId = die,
                    TorqueNm = 2100, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "D2676 400 KM", PowerHP = 400, PowerKW = 294, Displacement = 12419, FuelTypeId = die,
                    TorqueNm = 1900, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
            ]);

            int tgm = GetOrCreateModel(bId, "TGM", "man-tgm");
            AddEngines(GetOrCreateGeneration(tgm, "TGM (2019–)", "man-tgm-2019", 2019, null), [
                new EngineVersion { EngineName = "D2066 320 KM", PowerHP = 320, PowerKW = 235, Displacement = 10518, FuelTypeId = die,
                    TorqueNm = 1550, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "D0836 290 KM", PowerHP = 290, PowerKW = 213, Displacement = 6871, FuelTypeId = die,
                    TorqueNm = 1200, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "D0836 250 KM", PowerHP = 250, PowerKW = 184, Displacement = 6871, FuelTypeId = die,
                    TorqueNm = 1000, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
            ]);
        }

        // ── FORD TRUCKS ──────────────────────────────────────────────────────────
        {
            int bId = GetOrCreateBrand("Ford Trucks", "ford-trucks", "ciezarowe");
            if (BrandNeedsModels(bId)) { seededModelBrandIds.Add(bId); }

            int fmax = GetOrCreateModel(bId, "F-MAX", "ford-trucks-fmax");
            AddEngines(GetOrCreateGeneration(fmax, "F-MAX (2019–)", "ford-trucks-fmax-2019", 2019, null), [
                new EngineVersion { EngineName = "Ecotorq 12.7 500 KM", PowerHP = 500, PowerKW = 368, Displacement = 12739, FuelTypeId = die,
                    TorqueNm = 2500, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "Ecotorq 12.7 430 KM", PowerHP = 430, PowerKW = 316, Displacement = 12739, FuelTypeId = die,
                    TorqueNm = 2100, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
            ]);

            int cargo = GetOrCreateModel(bId, "Cargo 1846T", "ford-trucks-cargo1846t");
            AddEngines(GetOrCreateGeneration(cargo, "Cargo 1846T (2019–)", "ford-trucks-cargo1846t-2019", 2019, null), [
                new EngineVersion { EngineName = "Ecotorq 12.7 460 KM", PowerHP = 460, PowerKW = 338, Displacement = 12739, FuelTypeId = die,
                    TorqueNm = 2300, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
            ]);
        }

        // ── ALFA ROMEO (classic/popular models missing from initial seed) ───────
        {
            int bId = GetOrCreateBrand("Alfa Romeo", "alfa-romeo", "auta-osobowe");

            int m147 = GetOrCreateModel(bId, "147", "alfa-romeo-147");
            PrepareGenerations(m147,
                ("I (2000–2004)", "alfa-147-i", 2000, 2004),
                ("II (2004–2010)", "alfa-147-ii", 2004, 2010));
            AddOrReplaceEngines(GetOrFixGeneration(m147, "I (2000–2004)", "alfa-147-i", 2000, 2004), 85, [
                new EngineVersion { EngineName = "1.6 TS 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 144, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 190, FuelConsumptionCombined = 8.2m },
                new EngineVersion { EngineName = "2.0 TS 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1970, FuelTypeId = ben,
                    TorqueNm = 187, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.2m, TopSpeedKmh = 215, FuelConsumptionCombined = 9.5m },
                new EngineVersion { EngineName = "GTA 3.2 V6 250 KM", PowerHP = 250, PowerKW = 184, Displacement = 3179, FuelTypeId = ben,
                    TorqueNm = 300, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 6.3m, TopSpeedKmh = 250, FuelConsumptionCombined = 13.5m },
                new EngineVersion { EngineName = "1.9 JTD 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1910, FuelTypeId = die,
                    TorqueNm = 270, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.8m, TopSpeedKmh = 193, FuelConsumptionCombined = 5.4m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(m147, "II (2004–2010)", "alfa-147-ii", 2004, 2010), 85, [
                new EngineVersion { EngineName = "1.6 TS 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 144, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 190, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "2.0 JTS 165 KM", PowerHP = 165, PowerKW = 121, Displacement = 1970, FuelTypeId = ben,
                    TorqueNm = 205, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.9m, TopSpeedKmh = 218, FuelConsumptionCombined = 9.2m },
                new EngineVersion { EngineName = "1.9 JTDm 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1910, FuelTypeId = die,
                    TorqueNm = 280, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.3m, TopSpeedKmh = 195, FuelConsumptionCombined = 5.3m },
                new EngineVersion { EngineName = "1.9 JTDm 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1910, FuelTypeId = die,
                    TorqueNm = 305, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.9m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.6m },
            ]);

            int m156 = GetOrCreateModel(bId, "156", "alfa-romeo-156");
            PrepareGenerations(m156,
                ("I (1997–2003)", "alfa-156-i", 1997, 2003),
                ("II (2003–2007)", "alfa-156-ii", 2003, 2007));
            AddOrReplaceEngines(GetOrFixGeneration(m156, "I (1997–2003)", "alfa-156-i", 1997, 2003), 100, [
                new EngineVersion { EngineName = "1.6 TS 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 144, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 195, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "1.8 TS 144 KM", PowerHP = 144, PowerKW = 106, Displacement = 1747, FuelTypeId = ben,
                    TorqueNm = 172, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.4m, TopSpeedKmh = 210, FuelConsumptionCombined = 9.2m },
                new EngineVersion { EngineName = "2.0 TS 155 KM", PowerHP = 155, PowerKW = 114, Displacement = 1970, FuelTypeId = ben,
                    TorqueNm = 187, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.9m, TopSpeedKmh = 215, FuelConsumptionCombined = 9.8m },
                new EngineVersion { EngineName = "2.5 V6 24V 192 KM", PowerHP = 192, PowerKW = 141, Displacement = 2492, FuelTypeId = ben,
                    TorqueNm = 222, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 7.0m, TopSpeedKmh = 230, FuelConsumptionCombined = 11.5m },
                new EngineVersion { EngineName = "GTA 3.2 V6 250 KM", PowerHP = 250, PowerKW = 184, Displacement = 3179, FuelTypeId = ben,
                    TorqueNm = 300, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 6.3m, TopSpeedKmh = 250, FuelConsumptionCombined = 12.8m },
                new EngineVersion { EngineName = "1.9 JTD 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 1910, FuelTypeId = die,
                    TorqueNm = 200, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.7m, TopSpeedKmh = 190, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "1.9 JTD 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1910, FuelTypeId = die,
                    TorqueNm = 305, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.7m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(m156, "II (2003–2007)", "alfa-156-ii", 2003, 2007), 100, [
                new EngineVersion { EngineName = "1.6 TS 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 144, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 195, FuelConsumptionCombined = 8.3m },
                new EngineVersion { EngineName = "2.0 JTS 165 KM", PowerHP = 165, PowerKW = 121, Displacement = 1970, FuelTypeId = ben,
                    TorqueNm = 205, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.7m, TopSpeedKmh = 218, FuelConsumptionCombined = 9.4m },
                new EngineVersion { EngineName = "2.5 V6 24V 192 KM", PowerHP = 192, PowerKW = 141, Displacement = 2492, FuelTypeId = ben,
                    TorqueNm = 222, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 7.0m, TopSpeedKmh = 230, FuelConsumptionCombined = 11.3m },
                new EngineVersion { EngineName = "1.9 JTDm 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1910, FuelTypeId = die,
                    TorqueNm = 275, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 193, FuelConsumptionCombined = 5.3m },
                new EngineVersion { EngineName = "1.9 JTDm 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1910, FuelTypeId = die,
                    TorqueNm = 305, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "2.4 JTDm 175 KM", PowerHP = 175, PowerKW = 129, Displacement = 2387, FuelTypeId = die,
                    TorqueNm = 370, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 5, Acceleration0100 = 7.6m, TopSpeedKmh = 220, FuelConsumptionCombined = 6.1m },
            ]);

            int mito = GetOrCreateModel(bId, "MiTo", "alfa-romeo-mito");
            AddEngines(GetOrCreateGeneration(mito, "955 (2008–2018)", "alfa-mito-955", 2008, 2018), [
                new EngineVersion { EngineName = "1.4 78 KM", PowerHP = 78, PowerKW = 57, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 118, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.5m, TopSpeedKmh = 167, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "1.4 T 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 178, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.2m, TopSpeedKmh = 200, FuelConsumptionCombined = 6.8m },
                new EngineVersion { EngineName = "1.4 TB Quadrifoglio Verde 170 KM", PowerHP = 170, PowerKW = 125, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 230, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "1.3 JTDm 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 1248, FuelTypeId = die,
                    TorqueNm = 200, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 183, FuelConsumptionCombined = 4.4m },
            ]);

            int m4c = GetOrCreateModel(bId, "4C", "alfa-romeo-4c");
            AddEngines(GetOrCreateGeneration(m4c, "960 (2013–2020)", "alfa-4c-960", 2013, 2020), [
                new EngineVersion { EngineName = "1.75 TB 240 KM", PowerHP = 240, PowerKW = 177, Displacement = 1742, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 4.5m, TopSpeedKmh = 258, FuelConsumptionCombined = 7.1m },
            ]);

            int m145 = GetOrCreateModel(bId, "145", "alfa-romeo-145");
            AddOrReplaceEngines(GetOrFixGeneration(m145, "I (1994–2001)", "alfa-145-i", 1994, 2001), 60, [
                new EngineVersion { EngineName = "1.6 TS 103 KM", PowerHP = 103, PowerKW = 76, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 137, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.8m, TopSpeedKmh = 187, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "1.8 TS 144 KM", PowerHP = 144, PowerKW = 106, Displacement = 1747, FuelTypeId = ben,
                    TorqueNm = 169, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.0m, TopSpeedKmh = 205, FuelConsumptionCombined = 8.6m },
                new EngineVersion { EngineName = "2.0 TS 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1970, FuelTypeId = ben,
                    TorqueNm = 181, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 210, FuelConsumptionCombined = 9.3m },
                new EngineVersion { EngineName = "1.9 TD 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1929, FuelTypeId = die,
                    TorqueNm = 181, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.2m, TopSpeedKmh = 178, FuelConsumptionCombined = 5.9m },
            ]);

            int m146 = GetOrCreateModel(bId, "146", "alfa-romeo-146");
            AddOrReplaceEngines(GetOrFixGeneration(m146, "I (1994–2001)", "alfa-146-i", 1994, 2001), 60, [
                new EngineVersion { EngineName = "1.6 TS 103 KM", PowerHP = 103, PowerKW = 76, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 137, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 186, FuelConsumptionCombined = 8.1m },
                new EngineVersion { EngineName = "1.8 TS 144 KM", PowerHP = 144, PowerKW = 106, Displacement = 1747, FuelTypeId = ben,
                    TorqueNm = 169, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.2m, TopSpeedKmh = 204, FuelConsumptionCombined = 8.7m },
                new EngineVersion { EngineName = "2.0 TS 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1970, FuelTypeId = ben,
                    TorqueNm = 181, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.2m, TopSpeedKmh = 209, FuelConsumptionCombined = 9.4m },
                new EngineVersion { EngineName = "1.9 TD 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1929, FuelTypeId = die,
                    TorqueNm = 181, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.4m, TopSpeedKmh = 177, FuelConsumptionCombined = 6.0m },
            ]);

            int m159 = GetOrCreateModel(bId, "159", "alfa-romeo-159");
            AddOrReplaceEngines(GetOrFixGeneration(m159, "I (2005–2011)", "alfa-159-i", 2005, 2011), 115, [
                new EngineVersion { EngineName = "1.9 JTS 160 KM", PowerHP = 160, PowerKW = 118, Displacement = 1859, FuelTypeId = ben,
                    TorqueNm = 190, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.3m, TopSpeedKmh = 210, FuelConsumptionCombined = 9.1m },
                new EngineVersion { EngineName = "1.8 TBi 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 1742, FuelTypeId = ben,
                    TorqueNm = 320, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 220, FuelConsumptionCombined = 8.7m },
                new EngineVersion { EngineName = "3.2 JTS V6 260 KM", PowerHP = 260, PowerKW = 191, Displacement = 3195, FuelTypeId = ben,
                    TorqueNm = 322, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.6m, TopSpeedKmh = 245, FuelConsumptionCombined = 11.9m },
                new EngineVersion { EngineName = "1.9 JTDm 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1910, FuelTypeId = die,
                    TorqueNm = 280, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.9m, TopSpeedKmh = 195, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "2.4 JTDm 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 2387, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 5, Acceleration0100 = 8.0m, TopSpeedKmh = 222, FuelConsumptionCombined = 6.7m },
            ]);

            int m166 = GetOrCreateModel(bId, "166", "alfa-romeo-166");
            PrepareGenerations(m166,
                ("I (1998–2003)", "alfa-166-i", 1998, 2003),
                ("II (2003–2007)", "alfa-166-ii", 2003, 2007));
            AddOrReplaceEngines(GetOrFixGeneration(m166, "I (1998–2003)", "alfa-166-i", 1998, 2003), 130, [
                new EngineVersion { EngineName = "2.0 TS 155 KM", PowerHP = 155, PowerKW = 114, Displacement = 1970, FuelTypeId = ben,
                    TorqueNm = 187, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 210, FuelConsumptionCombined = 10.2m },
                new EngineVersion { EngineName = "2.5 V6 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 2492, FuelTypeId = ben,
                    TorqueNm = 222, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 8.2m, TopSpeedKmh = 226, FuelConsumptionCombined = 11.8m },
                new EngineVersion { EngineName = "3.0 V6 226 KM", PowerHP = 226, PowerKW = 166, Displacement = 2959, FuelTypeId = ben,
                    TorqueNm = 275, EuroNorm = "Euro 3", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 7.3m, TopSpeedKmh = 240, FuelConsumptionCombined = 12.7m },
                new EngineVersion { EngineName = "2.4 JTD 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 2387, FuelTypeId = die,
                    TorqueNm = 304, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 5, Acceleration0100 = 10.9m, TopSpeedKmh = 200, FuelConsumptionCombined = 6.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(m166, "II (2003–2007)", "alfa-166-ii", 2003, 2007), 130, [
                new EngineVersion { EngineName = "2.0 TS 165 KM", PowerHP = 165, PowerKW = 121, Displacement = 1970, FuelTypeId = ben,
                    TorqueNm = 191, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.5m, TopSpeedKmh = 212, FuelConsumptionCombined = 10.0m },
                new EngineVersion { EngineName = "3.2 V6 240 KM", PowerHP = 240, PowerKW = 177, Displacement = 3179, FuelTypeId = ben,
                    TorqueNm = 300, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 7.0m, TopSpeedKmh = 244, FuelConsumptionCombined = 12.9m },
                new EngineVersion { EngineName = "2.4 JTD 175 KM", PowerHP = 175, PowerKW = 129, Displacement = 2387, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 5, Acceleration0100 = 9.0m, TopSpeedKmh = 213, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "2.4 JTD 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 2387, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 5, Acceleration0100 = 8.3m, TopSpeedKmh = 222, FuelConsumptionCombined = 6.9m },
            ]);

            int brera = GetOrCreateModel(bId, "Brera", "alfa-romeo-brera");
            AddOrReplaceEngines(GetOrFixGeneration(brera, "I (2005–2010)", "alfa-brera-i", 2005, 2010), 175, [
                new EngineVersion { EngineName = "2.2 JTS 185 KM", PowerHP = 185, PowerKW = 136, Displacement = 2198, FuelTypeId = ben,
                    TorqueNm = 216, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.9m, TopSpeedKmh = 220, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "3.2 V6 260 KM", PowerHP = 260, PowerKW = 191, Displacement = 3195, FuelTypeId = ben,
                    TorqueNm = 322, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 6.8m, TopSpeedKmh = 245, FuelConsumptionCombined = 12.7m },
                new EngineVersion { EngineName = "3.2 V6 Q4 260 KM", PowerHP = 260, PowerKW = 191, Displacement = 3195, FuelTypeId = ben,
                    TorqueNm = 322, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.6m, TopSpeedKmh = 242, FuelConsumptionCombined = 13.2m },
                new EngineVersion { EngineName = "2.4 JTDm 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 2387, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 5, Acceleration0100 = 8.0m, TopSpeedKmh = 222, FuelConsumptionCombined = 6.8m },
                new EngineVersion { EngineName = "2.4 JTDm Q4 210 KM", PowerHP = 210, PowerKW = 154, Displacement = 2387, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 5, Acceleration0100 = 7.9m, TopSpeedKmh = 220, FuelConsumptionCombined = 7.1m },
            ]);

            int gt = GetOrCreateModel(bId, "GT", "alfa-romeo-gt");
            AddOrReplaceEngines(GetOrFixGeneration(gt, "I (2003–2010)", "alfa-gt-i", 2003, 2010), 130, [
                new EngineVersion { EngineName = "1.8 TS 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1747, FuelTypeId = ben,
                    TorqueNm = 165, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.6m, TopSpeedKmh = 205, FuelConsumptionCombined = 9.2m },
                new EngineVersion { EngineName = "2.0 JTS 165 KM", PowerHP = 165, PowerKW = 121, Displacement = 1970, FuelTypeId = ben,
                    TorqueNm = 206, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 220, FuelConsumptionCombined = 9.6m },
                new EngineVersion { EngineName = "3.2 V6 240 KM", PowerHP = 240, PowerKW = 177, Displacement = 3179, FuelTypeId = ben,
                    TorqueNm = 300, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 6.5m, TopSpeedKmh = 245, FuelConsumptionCombined = 12.5m },
                new EngineVersion { EngineName = "1.9 JTD 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1910, FuelTypeId = die,
                    TorqueNm = 305, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.0m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.8m },
                new EngineVersion { EngineName = "2.0 JTD 170 KM", PowerHP = 170, PowerKW = 125, Displacement = 1998, FuelTypeId = die,
                    TorqueNm = 360, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.6m, TopSpeedKmh = 215, FuelConsumptionCombined = 6.2m },
            ]);

            int gtv = GetOrCreateModel(bId, "GTV", "alfa-romeo-gtv");
            AddOrReplaceEngines(GetOrFixGeneration(gtv, "I (1995–2005)", "alfa-gtv-i", 1995, 2005), 130, [
                new EngineVersion { EngineName = "2.0 TS 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1970, FuelTypeId = ben,
                    TorqueNm = 181, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.9m, TopSpeedKmh = 210, FuelConsumptionCombined = 9.8m },
                new EngineVersion { EngineName = "2.0 V6 TB 202 KM", PowerHP = 202, PowerKW = 149, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 270, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 6.8m, TopSpeedKmh = 240, FuelConsumptionCombined = 11.2m },
                new EngineVersion { EngineName = "3.0 V6 24V 220 KM", PowerHP = 220, PowerKW = 162, Displacement = 2959, FuelTypeId = ben,
                    TorqueNm = 260, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 6.5m, TopSpeedKmh = 245, FuelConsumptionCombined = 12.6m },
                new EngineVersion { EngineName = "3.2 V6 24V 240 KM", PowerHP = 240, PowerKW = 177, Displacement = 3179, FuelTypeId = ben,
                    TorqueNm = 300, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 6.3m, TopSpeedKmh = 250, FuelConsumptionCombined = 12.9m },
            ]);

            int spider = GetOrCreateModel(bId, "Spider", "alfa-romeo-spider");
            AddOrReplaceEngines(GetOrFixGeneration(spider, "I (1995–2005)", "alfa-spider-i", 1995, 2005), 130, [
                new EngineVersion { EngineName = "2.0 TS 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1970, FuelTypeId = ben,
                    TorqueNm = 181, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.0m, TopSpeedKmh = 208, FuelConsumptionCombined = 9.9m },
                new EngineVersion { EngineName = "2.0 V6 TB 202 KM", PowerHP = 202, PowerKW = 149, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 270, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 7.0m, TopSpeedKmh = 235, FuelConsumptionCombined = 11.4m },
                new EngineVersion { EngineName = "3.0 V6 24V 220 KM", PowerHP = 220, PowerKW = 162, Displacement = 2959, FuelTypeId = ben,
                    TorqueNm = 260, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 6.7m, TopSpeedKmh = 242, FuelConsumptionCombined = 12.8m },
                new EngineVersion { EngineName = "3.2 V6 24V 240 KM", PowerHP = 240, PowerKW = 177, Displacement = 3179, FuelTypeId = ben,
                    TorqueNm = 300, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 6.4m, TopSpeedKmh = 248, FuelConsumptionCombined = 13.1m },
            ]);
        }

        // ── HARLEY-DAVIDSON (additional popular models) ──────────────────────────
        {
            int bId = GetOrCreateBrand("Harley-Davidson", "harley-davidson", "motocykle");

            int iron883 = GetOrCreateModel(bId, "Iron 883", "hd-iron-883");
            AddEngines(GetOrCreateGeneration(iron883, "XL883N (2009–)", "hd-iron883-xl883n", 2009, null), [
                new EngineVersion { EngineName = "Sportster Evolution 883cc V-Twin 50 KM", PowerHP = 50, PowerKW = 37, Displacement = 883, FuelTypeId = ben,
                    TorqueNm = 68, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, TopSpeedKmh = 165, FuelConsumptionCombined = 5.5m },
            ]);

            int iron1200 = GetOrCreateModel(bId, "Iron 1200", "hd-iron-1200");
            AddEngines(GetOrCreateGeneration(iron1200, "XL1200NS (2018–)", "hd-iron1200-xl1200ns", 2018, null), [
                new EngineVersion { EngineName = "Sportster Evolution 1200cc V-Twin 67 KM", PowerHP = 67, PowerKW = 49, Displacement = 1202, FuelTypeId = ben,
                    TorqueNm = 98, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, TopSpeedKmh = 175, FuelConsumptionCombined = 5.8m },
            ]);

            int streetBob = GetOrCreateModel(bId, "Street Bob 114", "hd-street-bob-114");
            AddEngines(GetOrCreateGeneration(streetBob, "FXBBS (2021–)", "hd-street-bob-fxbbs", 2021, null), [
                new EngineVersion { EngineName = "Milwaukee-Eight 114 V-Twin 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1868, FuelTypeId = ben,
                    TorqueNm = 155, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, TopSpeedKmh = 185, FuelConsumptionCombined = 6.4m },
            ]);

            int heritage = GetOrCreateModel(bId, "Heritage Classic 114", "hd-heritage-classic-114");
            AddEngines(GetOrCreateGeneration(heritage, "FLHCS (2018–)", "hd-heritage-flhcs", 2018, null), [
                new EngineVersion { EngineName = "Milwaukee-Eight 114 V-Twin 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1868, FuelTypeId = ben,
                    TorqueNm = 155, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, TopSpeedKmh = 180, FuelConsumptionCombined = 7.0m },
            ]);

            int panAmerica = GetOrCreateModel(bId, "Pan America 1250", "hd-pan-america-1250");
            AddEngines(GetOrCreateGeneration(panAmerica, "RA1250 (2021–)", "hd-pan-america-ra1250", 2021, null), [
                new EngineVersion { EngineName = "Revolution Max 1250 V-Twin 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1252, FuelTypeId = ben,
                    TorqueNm = 128, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 2, Acceleration0100 = 4.5m, TopSpeedKmh = 220, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "Revolution Max 1250 Special 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1252, FuelTypeId = ben,
                    TorqueNm = 128, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 2, Acceleration0100 = 4.5m, TopSpeedKmh = 220, FuelConsumptionCombined = 6.5m },
            ]);

            int nightster = GetOrCreateModel(bId, "Nightster", "hd-nightster");
            AddEngines(GetOrCreateGeneration(nightster, "RH975 (2022–)", "hd-nightster-rh975", 2022, null), [
                new EngineVersion { EngineName = "Revolution Max 975T V-Twin 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 975, FuelTypeId = ben,
                    TorqueNm = 95, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, TopSpeedKmh = 180, FuelConsumptionCombined = 5.8m },
            ]);
        }

        // ── YAMAHA (additional moto models) ─────────────────────────────────────
        {
            int bId = GetOrCreateBrand("Yamaha", "yamaha", "motocykle");

            int xsr900 = GetOrCreateModel(bId, "XSR900", "yamaha-xsr900");
            AddEngines(GetOrCreateGeneration(xsr900, "XSR900 (2022–)", "yamaha-xsr900-2022", 2022, null), [
                new EngineVersion { EngineName = "890cc CP3 119 KM", PowerHP = 119, PowerKW = 88, Displacement = 890, FuelTypeId = ben,
                    TorqueNm = 93, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, Acceleration0100 = 3.8m, TopSpeedKmh = 225, FuelConsumptionCombined = 5.9m },
            ]);

            int r3 = GetOrCreateModel(bId, "YZF-R3", "yamaha-yzf-r3");
            AddEngines(GetOrCreateGeneration(r3, "RH12 (2019–)", "yamaha-r3-rh12", 2019, null), [
                new EngineVersion { EngineName = "321cc parallel twin 42 KM", PowerHP = 42, PowerKW = 31, Displacement = 321, FuelTypeId = ben,
                    TorqueNm = 29, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, TopSpeedKmh = 180, FuelConsumptionCombined = 4.0m },
            ]);

            int yzfR6 = GetOrCreateModel(bId, "YZF-R6", "yamaha-yzf-r6");
            AddEngines(GetOrCreateGeneration(yzfR6, "RJ27 (2017–2020)", "yamaha-r6-rj27", 2017, 2020), [
                new EngineVersion { EngineName = "599cc I4 122 KM", PowerHP = 122, PowerKW = 90, Displacement = 599, FuelTypeId = ben,
                    TorqueNm = 66, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 3.4m, TopSpeedKmh = 260, FuelConsumptionCombined = 6.0m },
            ]);

            int nmax = GetOrCreateModel(bId, "NMAX 125", "yamaha-nmax-125");
            AddEngines(GetOrCreateGeneration(nmax, "NMAX (2021–)", "yamaha-nmax-2021", 2021, null), [
                new EngineVersion { EngineName = "125cc single-cylinder 15 KM", PowerHP = 15, PowerKW = 11, Displacement = 125, FuelTypeId = ben,
                    TorqueNm = 11, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 1, TopSpeedKmh = 100, FuelConsumptionCombined = 2.2m },
            ]);
        }

        // ── DUCATI (additional models) ───────────────────────────────────────────
        {
            int bId = GetOrCreateBrand("Ducati", "ducati", "motocykle");

            int desert = GetOrCreateModel(bId, "DesertX", "ducati-desertx");
            AddEngines(GetOrCreateGeneration(desert, "DesertX (2022–)", "ducati-desertx-2022", 2022, null), [
                new EngineVersion { EngineName = "937cc Testastretta 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 937, FuelTypeId = ben,
                    TorqueNm = 92, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 4.2m, TopSpeedKmh = 220, FuelConsumptionCombined = 5.9m },
            ]);

            int hyper = GetOrCreateModel(bId, "Hypermotard 698", "ducati-hypermotard-698");
            AddEngines(GetOrCreateGeneration(hyper, "698 Mono (2024–)", "ducati-hyper698-2024", 2024, null), [
                new EngineVersion { EngineName = "659cc single-cylinder 77 KM", PowerHP = 77, PowerKW = 57, Displacement = 659, FuelTypeId = ben,
                    TorqueNm = 70, EuroNorm = "Euro 5+", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 1, Acceleration0100 = 3.9m, TopSpeedKmh = 235, FuelConsumptionCombined = 4.5m },
            ]);

            int diavel = GetOrCreateModel(bId, "Diavel V4", "ducati-diavel-v4");
            AddEngines(GetOrCreateGeneration(diavel, "Diavel V4 (2023–)", "ducati-diavel-v4-2023", 2023, null), [
                new EngineVersion { EngineName = "1158cc Granturismo V4 168 KM", PowerHP = 168, PowerKW = 124, Displacement = 1158, FuelTypeId = ben,
                    TorqueNm = 126, EuroNorm = "Euro 5+", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 3.0m, TopSpeedKmh = 241, FuelConsumptionCombined = 7.0m },
            ]);
        }

        // ── KTM (additional models) ──────────────────────────────────────────────
        {
            int bId = GetOrCreateBrand("KTM", "ktm", "motocykle");

            int ktm890 = GetOrCreateModel(bId, "890 Duke R", "ktm-890-duke-r");
            AddEngines(GetOrCreateGeneration(ktm890, "890 Duke R (2020–)", "ktm-890dr-2020", 2020, null), [
                new EngineVersion { EngineName = "889cc LC8c Parallel Twin 121 KM", PowerHP = 121, PowerKW = 89, Displacement = 889, FuelTypeId = ben,
                    TorqueNm = 99, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 3.7m, TopSpeedKmh = 240, FuelConsumptionCombined = 5.8m },
            ]);

            int ktm690 = GetOrCreateModel(bId, "690 Duke", "ktm-690-duke");
            AddEngines(GetOrCreateGeneration(ktm690, "690 Duke (2019–)", "ktm-690duke-2019", 2019, null), [
                new EngineVersion { EngineName = "693cc LC4 73 KM", PowerHP = 73, PowerKW = 54, Displacement = 693, FuelTypeId = ben,
                    TorqueNm = 73, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 1, Acceleration0100 = 4.9m, TopSpeedKmh = 200, FuelConsumptionCombined = 4.1m },
            ]);

            int adventure890 = GetOrCreateModel(bId, "890 Adventure", "ktm-890-adventure");
            AddEngines(GetOrCreateGeneration(adventure890, "890 Adventure (2021–)", "ktm-890adv-2021", 2021, null), [
                new EngineVersion { EngineName = "889cc LC8c Parallel Twin 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 889, FuelTypeId = ben,
                    TorqueNm = 100, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 4.2m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.0m },
                new EngineVersion { EngineName = "889cc LC8c Adventure R 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 889, FuelTypeId = ben,
                    TorqueNm = 100, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 4.2m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.0m },
            ]);
        }

        // ── SUZUKI (additional moto models) ─────────────────────────────────────
        {
            int bId = GetOrCreateBrand("Suzuki", "suzuki", "auta-osobowe", "motocykle");

            int vstrom650 = GetOrCreateModel(bId, "V-Strom 650", "suzuki-v-strom-650");
            AddEngines(GetOrCreateGeneration(vstrom650, "2017–", "suzuki-v-strom-650-2017", 2017, null), [
                new EngineVersion { EngineName = "645cc V-Twin 71 KM", PowerHP = 71, PowerKW = 52, Displacement = 645, FuelTypeId = ben,
                    TorqueNm = 62, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, TopSpeedKmh = 193, FuelConsumptionCombined = 4.7m },
            ]);

            int gsxs750 = GetOrCreateModel(bId, "GSX-S750", "suzuki-gsx-s750");
            AddEngines(GetOrCreateGeneration(gsxs750, "GSX-S750 (2017–)", "suzuki-gsx-s750-2017", 2017, null), [
                new EngineVersion { EngineName = "749cc I4 114 KM", PowerHP = 114, PowerKW = 84, Displacement = 749, FuelTypeId = ben,
                    TorqueNm = 81, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 3.8m, TopSpeedKmh = 240, FuelConsumptionCombined = 5.8m },
            ]);

            int gsxs1000 = GetOrCreateModel(bId, "GSX-S1000", "suzuki-gsx-s1000");
            AddEngines(GetOrCreateGeneration(gsxs1000, "2021–", "suzuki-gsx-s1000-2021", 2021, null), [
                new EngineVersion { EngineName = "999cc I4 152 KM", PowerHP = 152, PowerKW = 112, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 106, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 3.3m, TopSpeedKmh = 250, FuelConsumptionCombined = 6.1m },
            ]);

            int hayabusa = GetOrCreateModel(bId, "Hayabusa", "suzuki-hayabusa");
            AddEngines(GetOrCreateGeneration(hayabusa, "2021–", "suzuki-hayabusa-2021", 2021, null), [
                new EngineVersion { EngineName = "1340cc I4 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1340, FuelTypeId = ben,
                    TorqueNm = 150, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 2.5m, TopSpeedKmh = 299, FuelConsumptionCombined = 7.5m },
            ]);
        }

        // ── TRIUMPH (additional models) ─────────────────────────────────────────
        {
            int bId = GetOrCreateBrand("Triumph", "triumph", "motocykle");

            int street = GetOrCreateModel(bId, "Street Triple 765", "triumph-street-triple-765");
            AddEngines(GetOrCreateGeneration(street, "2017–", "triumph-street-triple-2017", 2017, null), [
                new EngineVersion { EngineName = "765cc inline-3 R 118 KM", PowerHP = 118, PowerKW = 87, Displacement = 765, FuelTypeId = ben,
                    TorqueNm = 77, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, Acceleration0100 = 3.5m, TopSpeedKmh = 240, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "765cc inline-3 RS 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 765, FuelTypeId = ben,
                    TorqueNm = 80, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, Acceleration0100 = 3.3m, TopSpeedKmh = 250, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "765cc inline-3 Moto2 Edition 128 KM", PowerHP = 128, PowerKW = 94, Displacement = 765, FuelTypeId = ben,
                    TorqueNm = 79, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, Acceleration0100 = 3.4m, TopSpeedKmh = 245, FuelConsumptionCombined = 5.5m },
            ]);

            int thruxton = GetOrCreateModel(bId, "Thruxton 1200", "triumph-thruxton-1200");
            AddEngines(GetOrCreateGeneration(thruxton, "Thruxton 1200 (2016–)", "triumph-thruxton1200-2016", 2016, null), [
                new EngineVersion { EngineName = "1200cc Parallel Twin 97 KM", PowerHP = 97, PowerKW = 71, Displacement = 1200, FuelTypeId = ben,
                    TorqueNm = 112, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, TopSpeedKmh = 200, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "1200cc Parallel Twin R 104 KM", PowerHP = 104, PowerKW = 77, Displacement = 1200, FuelTypeId = ben,
                    TorqueNm = 112, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, TopSpeedKmh = 203, FuelConsumptionCombined = 5.5m },
            ]);
        }

        // ── SCANIA (truck engines with full TorqueNm data) ───────────────────────
        {
            int bId = GetOrCreateBrand("Scania", "scania", "ciezarowe");

            int rSeries = GetOrCreateModel(bId, "R-Series", "scania-r-series");
            AddEngines(GetOrCreateGeneration(rSeries, "Next Gen (2016–)", "scania-r-nextgen", 2016, null), [
                new EngineVersion { EngineName = "DC09 320 KM", PowerHP = 320, PowerKW = 235, Displacement = 9290, FuelTypeId = die,
                    TorqueNm = 1600, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "DC13 410 KM", PowerHP = 410, PowerKW = 302, Displacement = 12742, FuelTypeId = die,
                    TorqueNm = 2000, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "DC13 460 KM", PowerHP = 460, PowerKW = 338, Displacement = 12742, FuelTypeId = die,
                    TorqueNm = 2300, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "DC13 500 KM", PowerHP = 500, PowerKW = 368, Displacement = 12742, FuelTypeId = die,
                    TorqueNm = 2500, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "DC16 590 KM", PowerHP = 590, PowerKW = 434, Displacement = 15607, FuelTypeId = die,
                    TorqueNm = 2950, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
            ]);

            int sSeries = GetOrCreateModel(bId, "S-Series", "scania-s-series");
            AddEngines(GetOrCreateGeneration(sSeries, "Next Gen (2016–)", "scania-s-nextgen", 2016, null), [
                new EngineVersion { EngineName = "DC13 410 KM", PowerHP = 410, PowerKW = 302, Displacement = 12742, FuelTypeId = die,
                    TorqueNm = 2000, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "DC13 460 KM", PowerHP = 460, PowerKW = 338, Displacement = 12742, FuelTypeId = die,
                    TorqueNm = 2300, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "DC13 500 KM", PowerHP = 500, PowerKW = 368, Displacement = 12742, FuelTypeId = die,
                    TorqueNm = 2500, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
                new EngineVersion { EngineName = "DC16 590 KM", PowerHP = 590, PowerKW = 434, Displacement = 15607, FuelTypeId = die,
                    TorqueNm = 2950, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD" },
            ]);
        }

        // ── JOHN DEERE (agricultural – TorqueNm enrichment) ──────────────────────
        {
            int bId = GetOrCreateBrand("John Deere", "john-deere", "rolnicze");

            int seria6 = GetOrCreateModel(bId, "Seria 6", "jd-seria-6");
            AddEngines(GetOrCreateGeneration(seria6, "6M/6R (2014–)", "jd-seria6-2014", 2014, null), [
                new EngineVersion { EngineName = "6110R 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 4530, FuelTypeId = die,
                    TorqueNm = 410, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD" },
                new EngineVersion { EngineName = "6130R 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 4530, FuelTypeId = die,
                    TorqueNm = 470, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD" },
                new EngineVersion { EngineName = "6155R 155 KM", PowerHP = 155, PowerKW = 114, Displacement = 6800, FuelTypeId = die,
                    TorqueNm = 560, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD" },
                new EngineVersion { EngineName = "6175R 175 KM", PowerHP = 175, PowerKW = 129, Displacement = 6800, FuelTypeId = die,
                    TorqueNm = 635, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD" },
            ]);

            int seria7 = GetOrCreateModel(bId, "Seria 7", "jd-seria-7");
            AddEngines(GetOrCreateGeneration(seria7, "7R (2014–)", "jd-seria7-2014", 2014, null), [
                new EngineVersion { EngineName = "7230R 230 KM", PowerHP = 230, PowerKW = 169, Displacement = 9000, FuelTypeId = die,
                    TorqueNm = 850, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD" },
                new EngineVersion { EngineName = "7260R 260 KM", PowerHP = 260, PowerKW = 191, Displacement = 9000, FuelTypeId = die,
                    TorqueNm = 975, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD" },
                new EngineVersion { EngineName = "7310R 310 KM", PowerHP = 310, PowerKW = 228, Displacement = 9000, FuelTypeId = die,
                    TorqueNm = 1150, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD" },
            ]);
        }

        // ── FENDT (agricultural – TorqueNm enrichment) ───────────────────────────
        {
            int bId = GetOrCreateBrand("Fendt", "fendt", "rolnicze");

            int vario700 = GetOrCreateModel(bId, "Vario 700", "fendt-vario-700");
            AddEngines(GetOrCreateGeneration(vario700, "Gen6 (2014–)", "fendt-700-gen6", 2014, null), [
                new EngineVersion { EngineName = "718 Vario 177 KM", PowerHP = 177, PowerKW = 130, Displacement = 6057, FuelTypeId = die,
                    TorqueNm = 680, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD" },
                new EngineVersion { EngineName = "720 Vario 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 6057, FuelTypeId = die,
                    TorqueNm = 730, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD" },
                new EngineVersion { EngineName = "724 Vario 240 KM", PowerHP = 240, PowerKW = 177, Displacement = 6057, FuelTypeId = die,
                    TorqueNm = 890, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD" },
                new EngineVersion { EngineName = "728 Vario 281 KM", PowerHP = 281, PowerKW = 207, Displacement = 6057, FuelTypeId = die,
                    TorqueNm = 1100, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD" },
            ]);

            int vario900 = GetOrCreateModel(bId, "Vario 900", "fendt-vario-900");
            AddEngines(GetOrCreateGeneration(vario900, "Gen6 (2014–)", "fendt-900-gen6", 2014, null), [
                new EngineVersion { EngineName = "927 Vario 270 KM", PowerHP = 270, PowerKW = 199, Displacement = 8400, FuelTypeId = die,
                    TorqueNm = 1000, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD" },
                new EngineVersion { EngineName = "930 Vario 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 8400, FuelTypeId = die,
                    TorqueNm = 1100, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD" },
                new EngineVersion { EngineName = "936 Vario 360 KM", PowerHP = 360, PowerKW = 265, Displacement = 8400, FuelTypeId = die,
                    TorqueNm = 1200, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD" },
                new EngineVersion { EngineName = "939 Vario 390 KM", PowerHP = 390, PowerKW = 287, Displacement = 8400, FuelTypeId = die,
                    TorqueNm = 1350, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD" },
            ]);
        }

        // ── NEW HOLLAND (agricultural – TorqueNm enrichment) ─────────────────────
        {
            int bId = GetOrCreateBrand("New Holland", "new-holland", "rolnicze");

            int t6 = GetOrCreateModel(bId, "T6", "nh-t6");
            AddEngines(GetOrCreateGeneration(t6, "T6.xxx (2014–)", "nh-t6-2014", 2014, null), [
                new EngineVersion { EngineName = "T6.140 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 4485, FuelTypeId = die,
                    TorqueNm = 530, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD" },
                new EngineVersion { EngineName = "T6.160 160 KM", PowerHP = 160, PowerKW = 118, Displacement = 4485, FuelTypeId = die,
                    TorqueNm = 590, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD" },
                new EngineVersion { EngineName = "T6.180 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 6728, FuelTypeId = die,
                    TorqueNm = 660, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD" },
            ]);

            int t7 = GetOrCreateModel(bId, "T7", "nh-t7");
            AddEngines(GetOrCreateGeneration(t7, "T7.xxx (2014–)", "nh-t7-2014", 2014, null), [
                new EngineVersion { EngineName = "T7.175 175 KM", PowerHP = 175, PowerKW = 129, Displacement = 6728, FuelTypeId = die,
                    TorqueNm = 650, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD" },
                new EngineVersion { EngineName = "T7.210 210 KM", PowerHP = 210, PowerKW = 154, Displacement = 6728, FuelTypeId = die,
                    TorqueNm = 790, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD" },
                new EngineVersion { EngineName = "T7.260 260 KM", PowerHP = 260, PowerKW = 191, Displacement = 8728, FuelTypeId = die,
                    TorqueNm = 975, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD" },
            ]);
        }

        // ── VW GOLF (TorqueNm enrichment for most popular car in Poland) ─────────
        {
            int bId = GetOrCreateBrand("Volkswagen", "volkswagen", "auta-osobowe");

            int golf = GetOrCreateModel(bId, "Golf", "vw-golf");
            AddEngines(GetOrCreateGeneration(golf, "Mk7 (2012–2019)", "vw-golf-mk7", 2012, 2019), [
                new EngineVersion { EngineName = "1.4 TSI 125 KM", PowerHP = 125, PowerKW = 92, Displacement = 1395, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 203, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "1.5 TSI 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1498, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.4m, TopSpeedKmh = 210, FuelConsumptionCombined = 6.3m },
                new EngineVersion { EngineName = "2.0 GTI TSI 220 KM", PowerHP = 220, PowerKW = 162, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.5m, TopSpeedKmh = 246, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "1.6 TDI 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.5m, TopSpeedKmh = 198, FuelConsumptionCombined = 4.2m },
                new EngineVersion { EngineName = "2.0 TDI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 340, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 215, FuelConsumptionCombined = 4.8m },
            ]);
            AddEngines(GetOrCreateGeneration(golf, "Mk8 (2019–)", "vw-golf-mk8", 2019, null), [
                new EngineVersion { EngineName = "1.0 eTSI 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 999, FuelTypeId = mild,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.8m, TopSpeedKmh = 194, FuelConsumptionCombined = 5.7m },
                new EngineVersion { EngineName = "1.5 eTSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1498, FuelTypeId = mild,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.3m, TopSpeedKmh = 224, FuelConsumptionCombined = 6.3m },
                new EngineVersion { EngineName = "2.0 GTI TSI 245 KM", PowerHP = 245, PowerKW = 180, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 370, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.3m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "2.0 TDI 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 201, FuelConsumptionCombined = 4.2m },
                new EngineVersion { EngineName = "2.0 TDI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 360, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 215, FuelConsumptionCombined = 4.5m },
                new EngineVersion { EngineName = "GTE eHybrid 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 1395, FuelTypeId = phev,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.4m, TopSpeedKmh = 222, FuelConsumptionCombined = 1.6m },
            ]);

            int passat = GetOrCreateModel(bId, "Passat", "vw-passat");
            AddEngines(GetOrCreateGeneration(passat, "B8 (2014–2023)", "vw-passat-b8", 2014, 2023), [
                new EngineVersion { EngineName = "1.5 TSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1498, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.6m, TopSpeedKmh = 217, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "2.0 TSI 220 KM", PowerHP = 220, PowerKW = 162, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.5m, TopSpeedKmh = 237, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "2.0 TDI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 340, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.3m, TopSpeedKmh = 220, FuelConsumptionCombined = 4.5m },
                new EngineVersion { EngineName = "2.0 TDI 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.7m, TopSpeedKmh = 228, FuelConsumptionCombined = 5.3m },
                new EngineVersion { EngineName = "GTE 218 KM", PowerHP = 218, PowerKW = 160, Displacement = 1395, FuelTypeId = phev,
                    TorqueNm = 400, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.4m, TopSpeedKmh = 225, FuelConsumptionCombined = 1.7m },
            ]);
        }

        // ── SKODA OCTAVIA (TorqueNm enrichment) ──────────────────────────────────
        {
            int bId = GetOrCreateBrand("Skoda", "skoda", "auta-osobowe");

            int octavia = GetOrCreateModel(bId, "Octavia", "skoda-octavia");
            AddEngines(GetOrCreateGeneration(octavia, "III (2012–2020)", "skoda-octavia-iii", 2012, 2020), [
                new EngineVersion { EngineName = "1.4 TSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1395, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 220, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "2.0 TSI RS 230 KM", PowerHP = 230, PowerKW = 169, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.8m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.7m },
                new EngineVersion { EngineName = "1.6 TDI 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.8m, TopSpeedKmh = 195, FuelConsumptionCombined = 3.9m },
                new EngineVersion { EngineName = "2.0 TDI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 340, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.2m, TopSpeedKmh = 215, FuelConsumptionCombined = 4.5m },
            ]);
            AddEngines(GetOrCreateGeneration(octavia, "IV (2020–)", "skoda-octavia-iv", 2020, null), [
                new EngineVersion { EngineName = "1.5 TSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1498, FuelTypeId = mild,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.2m, TopSpeedKmh = 225, FuelConsumptionCombined = 6.1m },
                new EngineVersion { EngineName = "2.0 TSI RS 245 KM", PowerHP = 245, PowerKW = 180, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 370, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.7m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "2.0 TDI 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.2m, TopSpeedKmh = 198, FuelConsumptionCombined = 4.0m },
                new EngineVersion { EngineName = "2.0 TDI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 360, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.1m, TopSpeedKmh = 218, FuelConsumptionCombined = 4.3m },
                new EngineVersion { EngineName = "iV PHEV 245 KM", PowerHP = 245, PowerKW = 180, Displacement = 1395, FuelTypeId = phev,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.3m, TopSpeedKmh = 220, FuelConsumptionCombined = 1.4m },
            ]);
        }

        // ── FORD FOCUS / OPEL ASTRA (TorqueNm enrichment) ────────────────────────
        {
            int fordId = GetOrCreateBrand("Ford", "ford", "auta-osobowe");
            int focus = GetOrCreateModel(fordId, "Focus", "ford-focus");
            PrepareGenerations(focus,
                ("Mk1 (1998–2004)", "ford-focus-mk1", 1998, 2004),
                ("Mk2 (2004–2011)", "ford-focus-mk2", 2004, 2011),
                ("Mk3 (2011–2018)", "ford-focus-mk3", 2011, 2018),
                ("Mk4 (2018–)", "ford-focus-mk4", 2018, null));
            AddOrReplaceEngines(GetOrFixGeneration(focus, "Mk1 (1998–2004)", "ford-focus-mk1", 1998, 2004), 60, [
                new EngineVersion { EngineName = "1.4 75 KM", PowerHP = 75, PowerKW = 55, Displacement = 1388, FuelTypeId = ben,
                    TorqueNm = 122, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 15.0m, TopSpeedKmh = 165, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "1.6 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1596, FuelTypeId = ben,
                    TorqueNm = 145, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 180, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "ST170 2.0 170 KM", PowerHP = 170, PowerKW = 125, Displacement = 1988, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 217, FuelConsumptionCombined = 8.9m },
                new EngineVersion { EngineName = "1.8 TDCi 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1753, FuelTypeId = die,
                    TorqueNm = 240, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.8m, TopSpeedKmh = 180, FuelConsumptionCombined = 5.3m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(focus, "Mk2 (2004–2011)", "ford-focus-mk2", 2004, 2011), 70, [
                new EngineVersion { EngineName = "1.6 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1596, FuelTypeId = ben,
                    TorqueNm = 150, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.0m, TopSpeedKmh = 180, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "2.0 145 KM", PowerHP = 145, PowerKW = 107, Displacement = 1999, FuelTypeId = ben,
                    TorqueNm = 185, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.5m, TopSpeedKmh = 200, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "ST 2.5 Turbo 225 KM", PowerHP = 225, PowerKW = 165, Displacement = 2522, FuelTypeId = ben,
                    TorqueNm = 320, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 5, Acceleration0100 = 6.8m, TopSpeedKmh = 240, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "RS 2.5 Turbo 305 KM", PowerHP = 305, PowerKW = 224, Displacement = 2522, FuelTypeId = ben,
                    TorqueNm = 440, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 5, Acceleration0100 = 5.9m, TopSpeedKmh = 262, FuelConsumptionCombined = 12.0m },
                new EngineVersion { EngineName = "1.6 TDCi 109 KM", PowerHP = 109, PowerKW = 80, Displacement = 1560, FuelTypeId = die,
                    TorqueNm = 240, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 190, FuelConsumptionCombined = 4.5m },
                new EngineVersion { EngineName = "2.0 TDCi 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.2m, TopSpeedKmh = 205, FuelConsumptionCombined = 5.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(focus, "Mk4 (2018–)", "ford-focus-mk4", 2018, null), 90, [
                new EngineVersion { EngineName = "1.0 EcoBoost 125 KM", PowerHP = 125, PowerKW = 92, Displacement = 999, FuelTypeId = mild,
                    TorqueNm = 170, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.5m, TopSpeedKmh = 200, FuelConsumptionCombined = 5.3m },
                new EngineVersion { EngineName = "1.5 EcoBoost 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1499, FuelTypeId = mild,
                    TorqueNm = 240, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 213, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "2.3 ST 280 KM", PowerHP = 280, PowerKW = 206, Displacement = 2261, FuelTypeId = ben,
                    TorqueNm = 420, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 5.8m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "1.5 EcoBlue 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 280, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.5m, TopSpeedKmh = 205, FuelConsumptionCombined = 4.5m },
                new EngineVersion { EngineName = "2.0 EcoBlue 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 370, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.8m, TopSpeedKmh = 215, FuelConsumptionCombined = 4.8m },
            ]);

            int bronco = GetOrCreateModel(fordId, "Bronco", "ford-bronco");
            PrepareGenerations(bronco,
                ("I (1966–1996)", "ford-bronco-i", 1966, 1996),
                ("II (2021–)", "ford-bronco-ii", 2021, null));
            AddOrReplaceEngines(GetOrFixGeneration(bronco, "I (1966–1996)", "ford-bronco-i", 1966, 1996), 180, [
                new EngineVersion { EngineName = "5.0 V8 205 KM", PowerHP = 205, PowerKW = 151, Displacement = 4942, FuelTypeId = ben,
                    TorqueNm = 366, EuroNorm = "Euro 0", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 9.5m, TopSpeedKmh = 165, FuelConsumptionCombined = 18.0m },
                new EngineVersion { EngineName = "5.8 V8 210 KM", PowerHP = 210, PowerKW = 154, Displacement = 5766, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 0", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 9.0m, TopSpeedKmh = 170, FuelConsumptionCombined = 19.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(bronco, "II (2021–)", "ford-bronco-ii", 2021, null), 250, [
                new EngineVersion { EngineName = "2.3 EcoBoost 275 KM", PowerHP = 275, PowerKW = 202, Displacement = 2261, FuelTypeId = ben,
                    TorqueNm = 415, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.4m, TopSpeedKmh = 160, FuelConsumptionCombined = 11.5m },
                new EngineVersion { EngineName = "2.7 EcoBoost V6 335 KM", PowerHP = 335, PowerKW = 246, Displacement = 2694, FuelTypeId = ben,
                    TorqueNm = 555, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.5m, TopSpeedKmh = 170, FuelConsumptionCombined = 13.0m },
            ]);

            int cmax = GetOrCreateModel(fordId, "C-Max", "ford-c-max");
            PrepareGenerations(cmax,
                ("I (2003–2010)", "ford-c-max-i", 2003, 2010),
                ("II (2010–2019)", "ford-c-max-ii", 2010, 2019));
            AddOrReplaceEngines(GetOrFixGeneration(cmax, "I (2003–2010)", "ford-c-max-i", 2003, 2010), 90, [
                new EngineVersion { EngineName = "1.6 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1596, FuelTypeId = ben,
                    TorqueNm = 145, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.5m, TopSpeedKmh = 178, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "1.8 125 KM", PowerHP = 125, PowerKW = 92, Displacement = 1798, FuelTypeId = ben,
                    TorqueNm = 165, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.9m, TopSpeedKmh = 190, FuelConsumptionCombined = 7.8m },
                new EngineVersion { EngineName = "2.0 145 KM", PowerHP = 145, PowerKW = 107, Displacement = 1999, FuelTypeId = ben,
                    TorqueNm = 185, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 198, FuelConsumptionCombined = 8.2m },
                new EngineVersion { EngineName = "1.6 TDCi 109 KM", PowerHP = 109, PowerKW = 80, Displacement = 1560, FuelTypeId = die,
                    TorqueNm = 240, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.6m, TopSpeedKmh = 187, FuelConsumptionCombined = 4.8m },
                new EngineVersion { EngineName = "2.0 TDCi 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 200, FuelConsumptionCombined = 5.2m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(cmax, "II (2010–2019)", "ford-c-max-ii", 2010, 2019), 100, [
                new EngineVersion { EngineName = "1.0 EcoBoost 125 KM", PowerHP = 125, PowerKW = 92, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 170, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.4m, TopSpeedKmh = 190, FuelConsumptionCombined = 5.8m },
                new EngineVersion { EngineName = "1.6 TDCi 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1560, FuelTypeId = die,
                    TorqueNm = 270, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 190, FuelConsumptionCombined = 4.4m },
                new EngineVersion { EngineName = "2.0 TDCi 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 370, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.5m, TopSpeedKmh = 205, FuelConsumptionCombined = 4.9m },
            ]);

            int ecosport = GetOrCreateModel(fordId, "EcoSport", "ford-ecosport");
            PrepareGenerations(ecosport,
                ("I (2013–2017)", "ford-ecosport-i", 2013, 2017),
                ("II (2017–2022)", "ford-ecosport-ii", 2017, 2022));
            AddOrReplaceEngines(GetOrFixGeneration(ecosport, "I (2013–2017)", "ford-ecosport-i", 2013, 2017), 70, [
                new EngineVersion { EngineName = "1.0 EcoBoost 125 KM", PowerHP = 125, PowerKW = 92, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 170, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.4m, TopSpeedKmh = 180, FuelConsumptionCombined = 5.8m },
                new EngineVersion { EngineName = "1.5 82 KM", PowerHP = 82, PowerKW = 60, Displacement = 1499, FuelTypeId = ben,
                    TorqueNm = 140, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.9m, TopSpeedKmh = 158, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "1.5 TDCi 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 205, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.4m, TopSpeedKmh = 165, FuelConsumptionCombined = 4.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(ecosport, "II (2017–2022)", "ford-ecosport-ii", 2017, 2022), 90, [
                new EngineVersion { EngineName = "1.0 EcoBoost 125 KM", PowerHP = 125, PowerKW = 92, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 170, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.4m, TopSpeedKmh = 180, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "1.0 EcoBoost 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 3, Acceleration0100 = 9.8m, TopSpeedKmh = 188, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "1.5 TDCi 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 240, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.7m, TopSpeedKmh = 172, FuelConsumptionCombined = 4.6m },
                new EngineVersion { EngineName = "1.5 TDCi 125 KM", PowerHP = 125, PowerKW = 92, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 10.7m, TopSpeedKmh = 182, FuelConsumptionCombined = 4.9m },
            ]);

            int edge = GetOrCreateModel(fordId, "Edge", "ford-edge");
            PrepareGenerations(edge,
                ("I (2015–2018)", "ford-edge-i", 2015, 2018),
                ("II (2018–2023)", "ford-edge-ii", 2018, 2023));
            AddOrReplaceEngines(GetOrFixGeneration(edge, "I (2015–2018)", "ford-edge-i", 2015, 2018), 150, [
                new EngineVersion { EngineName = "2.0 TDCi 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 200, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "2.0 TDCi Bi-Turbo 210 KM", PowerHP = 210, PowerKW = 154, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 450, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.7m, TopSpeedKmh = 210, FuelConsumptionCombined = 6.9m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(edge, "II (2018–2023)", "ford-edge-ii", 2018, 2023), 180, [
                new EngineVersion { EngineName = "2.0 EcoBlue 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.0m, TopSpeedKmh = 205, FuelConsumptionCombined = 6.6m },
                new EngineVersion { EngineName = "2.0 EcoBlue Bi-Turbo 238 KM", PowerHP = 238, PowerKW = 175, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 470, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 212, FuelConsumptionCombined = 7.1m },
            ]);

            int explorer = GetOrCreateModel(fordId, "Explorer", "ford-explorer");
            PrepareGenerations(explorer,
                ("IV (2011–2019)", "ford-explorer-iv", 2011, 2019),
                ("V (2020–)", "ford-explorer-v", 2020, null));
            AddOrReplaceEngines(GetOrFixGeneration(explorer, "IV (2011–2019)", "ford-explorer-iv", 2011, 2019), 250, [
                new EngineVersion { EngineName = "3.5 V6 294 KM", PowerHP = 294, PowerKW = 216, Displacement = 3496, FuelTypeId = ben,
                    TorqueNm = 349, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 8.2m, TopSpeedKmh = 200, FuelConsumptionCombined = 13.5m },
                new EngineVersion { EngineName = "2.3 EcoBoost 280 KM", PowerHP = 280, PowerKW = 206, Displacement = 2261, FuelTypeId = ben,
                    TorqueNm = 425, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.9m, TopSpeedKmh = 195, FuelConsumptionCombined = 11.2m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(explorer, "V (2020–)", "ford-explorer-v", 2020, null), 280, [
                new EngineVersion { EngineName = "3.0 EcoBoost PHEV 457 KM", PowerHP = 457, PowerKW = 336, Displacement = 2956, FuelTypeId = phev,
                    TorqueNm = 678, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.9m, TopSpeedKmh = 220, FuelConsumptionCombined = 3.0m },
                new EngineVersion { EngineName = "2.3 EcoBoost 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 2261, FuelTypeId = ben,
                    TorqueNm = 425, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.4m, TopSpeedKmh = 200, FuelConsumptionCombined = 11.0m },
            ]);

            int galaxy = GetOrCreateModel(fordId, "Galaxy", "ford-galaxy");
            PrepareGenerations(galaxy,
                ("III (2006–2015)", "ford-galaxy-iii", 2006, 2015),
                ("IV (2015–)", "ford-galaxy-iv", 2015, null));
            AddOrReplaceEngines(GetOrFixGeneration(galaxy, "III (2006–2015)", "ford-galaxy-iii", 2006, 2015), 130, [
                new EngineVersion { EngineName = "1.6 EcoBoost 160 KM", PowerHP = 160, PowerKW = 118, Displacement = 1596, FuelTypeId = ben,
                    TorqueNm = 240, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.9m, TopSpeedKmh = 195, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "2.0 145 KM", PowerHP = 145, PowerKW = 107, Displacement = 1999, FuelTypeId = ben,
                    TorqueNm = 185, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 195, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "2.0 TDCi 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.9m, TopSpeedKmh = 195, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "2.0 TDCi 163 KM", PowerHP = 163, PowerKW = 120, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 360, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 205, FuelConsumptionCombined = 6.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(galaxy, "IV (2015–)", "ford-galaxy-iv", 2015, null), 130, [
                new EngineVersion { EngineName = "2.0 EcoBlue 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 370, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 197, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "2.0 EcoBlue 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.1m, TopSpeedKmh = 208, FuelConsumptionCombined = 6.2m },
            ]);

            int ka = GetOrCreateModel(fordId, "Ka", "ford-ka");
            PrepareGenerations(ka,
                ("I (1996–2008)", "ford-ka-i", 1996, 2008),
                ("II (2008–2016)", "ford-ka-ii", 2008, 2016));
            AddOrReplaceEngines(GetOrFixGeneration(ka, "I (1996–2008)", "ford-ka-i", 1996, 2008), 50, [
                new EngineVersion { EngineName = "1.3 60 KM", PowerHP = 60, PowerKW = 44, Displacement = 1299, FuelTypeId = ben,
                    TorqueNm = 104, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.7m, TopSpeedKmh = 150, FuelConsumptionCombined = 5.8m },
                new EngineVersion { EngineName = "1.3 70 KM", PowerHP = 70, PowerKW = 51, Displacement = 1299, FuelTypeId = ben,
                    TorqueNm = 110, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.4m, TopSpeedKmh = 157, FuelConsumptionCombined = 5.9m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(ka, "II (2008–2016)", "ford-ka-ii", 2008, 2016), 60, [
                new EngineVersion { EngineName = "1.2 69 KM", PowerHP = 69, PowerKW = 51, Displacement = 1242, FuelTypeId = ben,
                    TorqueNm = 102, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 15.0m, TopSpeedKmh = 155, FuelConsumptionCombined = 5.3m },
                new EngineVersion { EngineName = "1.3 TDCi 75 KM", PowerHP = 75, PowerKW = 55, Displacement = 1248, FuelTypeId = die,
                    TorqueNm = 190, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 15.5m, TopSpeedKmh = 155, FuelConsumptionCombined = 4.1m },
            ]);

            int maverick = GetOrCreateModel(fordId, "Maverick", "ford-maverick");
            AddOrReplaceEngines(GetOrFixGeneration(maverick, "I (2022–)", "ford-maverick-i", 2022, null), 180, [
                new EngineVersion { EngineName = "2.5 Hybrid 191 KM", PowerHP = 191, PowerKW = 140, Displacement = 2488, FuelTypeId = hyb,
                    TorqueNm = 210, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 170, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "2.0 EcoBoost 250 KM", PowerHP = 250, PowerKW = 184, Displacement = 1999, FuelTypeId = ben,
                    TorqueNm = 380, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.2m, TopSpeedKmh = 180, FuelConsumptionCombined = 10.0m },
            ]);

            int mondeo = GetOrCreateModel(fordId, "Mondeo", "ford-mondeo");
            PrepareGenerations(mondeo,
                ("I (1993–2000)", "ford-mondeo-i", 1993, 2000),
                ("III (2007–2014)", "ford-mondeo-iii", 2007, 2014),
                ("V (2014–2022)", "ford-mondeo-v", 2014, 2022));
            AddOrReplaceEngines(GetOrFixGeneration(mondeo, "I (1993–2000)", "ford-mondeo-i", 1993, 2000), 70, [
                new EngineVersion { EngineName = "1.6 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1597, FuelTypeId = ben,
                    TorqueNm = 133, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.5m, TopSpeedKmh = 175, FuelConsumptionCombined = 7.8m },
                new EngineVersion { EngineName = "1.8 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1796, FuelTypeId = ben,
                    TorqueNm = 155, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.8m, TopSpeedKmh = 195, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "2.0 131 KM", PowerHP = 131, PowerKW = 96, Displacement = 1988, FuelTypeId = ben,
                    TorqueNm = 175, EuroNorm = "Euro 2", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 205, FuelConsumptionCombined = 9.2m },
                new EngineVersion { EngineName = "1.8 TD 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1753, FuelTypeId = die,
                    TorqueNm = 190, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.5m, TopSpeedKmh = 170, FuelConsumptionCombined = 5.9m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(mondeo, "III (2007–2014)", "ford-mondeo-iii", 2007, 2014), 130, [
                new EngineVersion { EngineName = "1.6 EcoBoost 160 KM", PowerHP = 160, PowerKW = 118, Displacement = 1596, FuelTypeId = ben,
                    TorqueNm = 240, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.0m, TopSpeedKmh = 200, FuelConsumptionCombined = 6.9m },
                new EngineVersion { EngineName = "2.0 145 KM", PowerHP = 145, PowerKW = 107, Displacement = 1999, FuelTypeId = ben,
                    TorqueNm = 185, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 205, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "2.0 TDCi 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 205, FuelConsumptionCombined = 5.0m },
                new EngineVersion { EngineName = "2.2 TDCi 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 2179, FuelTypeId = die,
                    TorqueNm = 440, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.4m, TopSpeedKmh = 224, FuelConsumptionCombined = 5.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(mondeo, "V (2014–2022)", "ford-mondeo-v", 2014, 2022), 140, [
                new EngineVersion { EngineName = "1.5 EcoBoost 160 KM", PowerHP = 160, PowerKW = 118, Displacement = 1499, FuelTypeId = ben,
                    TorqueNm = 240, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.2m, TopSpeedKmh = 210, FuelConsumptionCombined = 6.3m },
                new EngineVersion { EngineName = "2.0 EcoBlue 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 370, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.4m, TopSpeedKmh = 212, FuelConsumptionCombined = 4.5m },
                new EngineVersion { EngineName = "2.0 EcoBlue 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.2m, TopSpeedKmh = 227, FuelConsumptionCombined = 5.0m },
                new EngineVersion { EngineName = "2.0 Hybrid 187 KM", PowerHP = 187, PowerKW = 138, Displacement = 1999, FuelTypeId = hyb,
                    TorqueNm = 173, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.3m, TopSpeedKmh = 187, FuelConsumptionCombined = 4.3m },
            ]);

            int mustang = GetOrCreateModel(fordId, "Mustang", "ford-mustang");
            AddOrReplaceEngines(GetOrFixGeneration(mustang, "V (2005–2014)", "ford-mustang-v", 2005, 2014), 200, [
                new EngineVersion { EngineName = "4.0 V6 213 KM", PowerHP = 213, PowerKW = 157, Displacement = 3984, FuelTypeId = ben,
                    TorqueNm = 325, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 7.5m, TopSpeedKmh = 195, FuelConsumptionCombined = 13.5m },
                new EngineVersion { EngineName = "4.6 V8 GT 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 4606, FuelTypeId = ben,
                    TorqueNm = 420, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 5.8m, TopSpeedKmh = 250, FuelConsumptionCombined = 15.5m },
                new EngineVersion { EngineName = "5.4 V8 Shelby GT500 500 KM", PowerHP = 500, PowerKW = 368, Displacement = 5408, FuelTypeId = ben,
                    TorqueNm = 664, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 4.6m, TopSpeedKmh = 250, FuelConsumptionCombined = 17.5m },
            ]);

            int puma = GetOrCreateModel(fordId, "Puma", "ford-puma");
            PrepareGenerations(puma,
                ("I (1997–2002)", "ford-puma-i", 1997, 2002),
                ("II (2019–)", "ford-puma-ii", 2019, null));
            AddOrReplaceEngines(GetOrFixGeneration(puma, "I (1997–2002)", "ford-puma-i", 1997, 2002), 60, [
                new EngineVersion { EngineName = "1.4 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1388, FuelTypeId = ben,
                    TorqueNm = 124, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.1m, TopSpeedKmh = 175, FuelConsumptionCombined = 6.9m },
                new EngineVersion { EngineName = "1.7 125 KM", PowerHP = 125, PowerKW = 92, Displacement = 1679, FuelTypeId = ben,
                    TorqueNm = 152, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.2m, TopSpeedKmh = 195, FuelConsumptionCombined = 7.6m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(puma, "II (2019–)", "ford-puma-ii", 2019, null), 100, [
                new EngineVersion { EngineName = "1.0 EcoBoost 125 KM", PowerHP = 125, PowerKW = 92, Displacement = 999, FuelTypeId = mild,
                    TorqueNm = 210, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.8m, TopSpeedKmh = 193, FuelConsumptionCombined = 5.3m },
                new EngineVersion { EngineName = "1.0 EcoBoost Hybrid 155 KM", PowerHP = 155, PowerKW = 114, Displacement = 999, FuelTypeId = mild,
                    TorqueNm = 240, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.0m, TopSpeedKmh = 200, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "ST 1.5 EcoBoost 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 1497, FuelTypeId = ben,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 6.7m, TopSpeedKmh = 220, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "1.5 EcoBlue 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.0m, TopSpeedKmh = 188, FuelConsumptionCombined = 4.3m },
            ]);

            int ranger = GetOrCreateModel(fordId, "Ranger", "ford-ranger");
            PrepareGenerations(ranger,
                ("II (2006–2011)", "ford-ranger-ii", 2006, 2011),
                ("III (2011–2022)", "ford-ranger-iii", 2011, 2022));
            AddOrReplaceEngines(GetOrFixGeneration(ranger, "II (2006–2011)", "ford-ranger-ii", 2006, 2011), 120, [
                new EngineVersion { EngineName = "2.5 TDCi 143 KM", PowerHP = 143, PowerKW = 105, Displacement = 2499, FuelTypeId = die,
                    TorqueNm = 330, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 12.5m, TopSpeedKmh = 165, FuelConsumptionCombined = 9.0m },
                new EngineVersion { EngineName = "3.0 TDCi 156 KM", PowerHP = 156, PowerKW = 115, Displacement = 2953, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 170, FuelConsumptionCombined = 9.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(ranger, "III (2011–2022)", "ford-ranger-iii", 2011, 2022), 150, [
                new EngineVersion { EngineName = "2.2 TDCi 160 KM", PowerHP = 160, PowerKW = 118, Displacement = 2198, FuelTypeId = die,
                    TorqueNm = 385, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 11.2m, TopSpeedKmh = 170, FuelConsumptionCombined = 7.8m },
                new EngineVersion { EngineName = "3.2 TDCi 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 3198, FuelTypeId = die,
                    TorqueNm = 470, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 5, Acceleration0100 = 10.5m, TopSpeedKmh = 180, FuelConsumptionCombined = 8.9m },
                new EngineVersion { EngineName = "2.0 EcoBlue Bi-Turbo 213 KM", PowerHP = 213, PowerKW = 157, Displacement = 1996, FuelTypeId = die,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 180, FuelConsumptionCombined = 8.2m },
            ]);

            int smax = GetOrCreateModel(fordId, "S-Max", "ford-s-max");
            PrepareGenerations(smax,
                ("I (2006–2015)", "ford-s-max-i", 2006, 2015),
                ("II (2015–2023)", "ford-s-max-ii", 2015, 2023));
            AddOrReplaceEngines(GetOrFixGeneration(smax, "I (2006–2015)", "ford-s-max-i", 2006, 2015), 130, [
                new EngineVersion { EngineName = "2.0 145 KM", PowerHP = 145, PowerKW = 107, Displacement = 1999, FuelTypeId = ben,
                    TorqueNm = 185, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 195, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "2.5 Turbo 220 KM", PowerHP = 220, PowerKW = 162, Displacement = 2522, FuelTypeId = ben,
                    TorqueNm = 310, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 5, Acceleration0100 = 8.4m, TopSpeedKmh = 220, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "2.0 TDCi 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.9m, TopSpeedKmh = 195, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "2.2 TDCi 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 2179, FuelTypeId = die,
                    TorqueNm = 440, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.7m, TopSpeedKmh = 220, FuelConsumptionCombined = 6.2m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(smax, "II (2015–2023)", "ford-s-max-ii", 2015, 2023), 130, [
                new EngineVersion { EngineName = "2.0 EcoBlue 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 370, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.6m, TopSpeedKmh = 197, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "2.0 EcoBlue 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.2m, TopSpeedKmh = 208, FuelConsumptionCombined = 6.3m },
            ]);

            // ── HONDA (Accord, HR-V, Jazz, e, ZR-V — Civic/CR-V already seeded by ModelSeeder) ──
            int hondaId2 = GetOrCreateBrand("Honda", "honda", "auta-osobowe");
            int accord = GetOrCreateModel(hondaId2, "Accord", "honda-accord");
            PrepareGenerations(accord,
                ("VIII (2008–2015)", "honda-accord-viii", 2008, 2015),
                ("IX (2015–2022)", "honda-accord-ix", 2015, 2022));
            AddOrReplaceEngines(GetOrFixGeneration(accord, "VIII (2008–2015)", "honda-accord-viii", 2008, 2015), 130, [
                new EngineVersion { EngineName = "2.0 i-VTEC 156 KM", PowerHP = 156, PowerKW = 115, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 190, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 210, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "2.4 i-VTEC 201 KM", PowerHP = 201, PowerKW = 148, Displacement = 2354, FuelTypeId = ben,
                    TorqueNm = 233, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.2m, TopSpeedKmh = 225, FuelConsumptionCombined = 8.9m },
                new EngineVersion { EngineName = "2.2 i-DTEC 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 2199, FuelTypeId = die,
                    TorqueNm = 350, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.4m, TopSpeedKmh = 215, FuelConsumptionCombined = 5.4m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(accord, "IX (2015–2022)", "honda-accord-ix", 2015, 2022), 140, [
                new EngineVersion { EngineName = "1.5 VTEC Turbo 173 KM", PowerHP = 173, PowerKW = 127, Displacement = 1498, FuelTypeId = ben,
                    TorqueNm = 220, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.1m, TopSpeedKmh = 210, FuelConsumptionCombined = 6.7m },
                new EngineVersion { EngineName = "2.0 e:HEV 215 KM", PowerHP = 215, PowerKW = 158, Displacement = 1993, FuelTypeId = hyb,
                    TorqueNm = 315, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.3m, TopSpeedKmh = 180, FuelConsumptionCombined = 5.2m },
            ]);

            int hrv = GetOrCreateModel(hondaId2, "HR-V", "honda-hrv");
            PrepareGenerations(hrv,
                ("II (2015–2021)", "honda-hrv-ii", 2015, 2021),
                ("III (2021–)", "honda-hrv-iii", 2021, null));
            AddOrReplaceEngines(GetOrFixGeneration(hrv, "II (2015–2021)", "honda-hrv-ii", 2015, 2021), 100, [
                new EngineVersion { EngineName = "1.5 i-VTEC 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1498, FuelTypeId = ben,
                    TorqueNm = 155, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.2m, TopSpeedKmh = 190, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "1.6 i-DTEC 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1597, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 192, FuelConsumptionCombined = 4.2m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(hrv, "III (2021–)", "honda-hrv-iii", 2021, null), 100, [
                new EngineVersion { EngineName = "e:HEV 1.5 131 KM", PowerHP = 131, PowerKW = 96, Displacement = 1498, FuelTypeId = hyb,
                    TorqueNm = 253, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.6m, TopSpeedKmh = 175, FuelConsumptionCombined = 5.4m },
            ]);

            int jazz = GetOrCreateModel(hondaId2, "Jazz", "honda-jazz");
            PrepareGenerations(jazz,
                ("III (2015–2020)", "honda-jazz-iii", 2015, 2020),
                ("IV (2020–)", "honda-jazz-iv", 2020, null));
            AddOrReplaceEngines(GetOrFixGeneration(jazz, "III (2015–2020)", "honda-jazz-iii", 2015, 2020), 90, [
                new EngineVersion { EngineName = "1.3 i-VTEC 102 KM", PowerHP = 102, PowerKW = 75, Displacement = 1317, FuelTypeId = ben,
                    TorqueNm = 123, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 175, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "1.5 i-VTEC 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1497, FuelTypeId = ben,
                    TorqueNm = 155, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.9m, TopSpeedKmh = 195, FuelConsumptionCombined = 5.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(jazz, "IV (2020–)", "honda-jazz-iv", 2020, null), 100, [
                new EngineVersion { EngineName = "e:HEV 1.5 109 KM", PowerHP = 109, PowerKW = 80, Displacement = 1498, FuelTypeId = hyb,
                    TorqueNm = 253, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.5m, TopSpeedKmh = 175, FuelConsumptionCombined = 4.5m },
                new EngineVersion { EngineName = "Crosstar e:HEV 1.5 109 KM", PowerHP = 109, PowerKW = 80, Displacement = 1498, FuelTypeId = hyb,
                    TorqueNm = 253, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.5m, TopSpeedKmh = 175, FuelConsumptionCombined = 4.6m },
            ]);

            int hondaE = GetOrCreateModel(hondaId2, "e", "honda-e");
            AddOrReplaceEngines(GetOrFixGeneration(hondaE, "I (2020–2023)", "honda-e-i", 2020, 2023), 100, [
                new EngineVersion { EngineName = "35.5 kWh 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 315, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "RWD",
                    Cylinders = 0, Acceleration0100 = 9.0m, TopSpeedKmh = 145, FuelConsumptionCombined = 17.2m },
                new EngineVersion { EngineName = "35.5 kWh Advance 154 KM", PowerHP = 154, PowerKW = 113, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 315, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "RWD",
                    Cylinders = 0, Acceleration0100 = 8.3m, TopSpeedKmh = 145, FuelConsumptionCombined = 17.5m },
            ]);

            int zrv = GetOrCreateModel(hondaId2, "ZR-V", "honda-zrv");
            AddOrReplaceEngines(GetOrFixGeneration(zrv, "I (2023–)", "honda-zrv-i", 2023, null), 130, [
                new EngineVersion { EngineName = "e:HEV 2.0 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 1993, FuelTypeId = hyb,
                    TorqueNm = 190, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.7m, TopSpeedKmh = 200, FuelConsumptionCombined = 5.3m },
                new EngineVersion { EngineName = "e:HEV Advance 2.0 184 KM AWD", PowerHP = 184, PowerKW = 135, Displacement = 1993, FuelTypeId = hyb,
                    TorqueNm = 190, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.7m, TopSpeedKmh = 200, FuelConsumptionCombined = 5.7m },
            ]);

            // ── HYUNDAI (Elantra, i20, Santa Fe, Ioniq 5, Bayon, i10 — i30/Tucson/Kona already seeded by ModelSeeder) ──
            int hyundaiId3 = GetOrCreateBrand("Hyundai", "hyundai", "auta-osobowe");
            int elantra = GetOrCreateModel(hyundaiId3, "Elantra", "hyundai-elantra");
            PrepareGenerations(elantra,
                ("AD (2015–2020)", "hyundai-elantra-ad", 2015, 2020),
                ("CN7 (2020–)", "hyundai-elantra-cn7", 2020, null));
            AddOrReplaceEngines(GetOrFixGeneration(elantra, "AD (2015–2020)", "hyundai-elantra-ad", 2015, 2020), 100, [
                new EngineVersion { EngineName = "1.6 GDI 132 KM", PowerHP = 132, PowerKW = 97, Displacement = 1591, FuelTypeId = ben,
                    TorqueNm = 165, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.9m, TopSpeedKmh = 190, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "1.6 CRDi 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.0m, TopSpeedKmh = 200, FuelConsumptionCombined = 4.4m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(elantra, "CN7 (2020–)", "hyundai-elantra-cn7", 2020, null), 120, [
                new EngineVersion { EngineName = "1.6 GDI 123 KM", PowerHP = 123, PowerKW = 90, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 154, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.9m, TopSpeedKmh = 185, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "1.6 T-GDI N Line 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 1591, FuelTypeId = ben,
                    TorqueNm = 265, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.9m, TopSpeedKmh = 240, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "1.6 Hybrid 141 KM", PowerHP = 141, PowerKW = 104, Displacement = 1580, FuelTypeId = hyb,
                    TorqueNm = 264, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.5m, TopSpeedKmh = 185, FuelConsumptionCombined = 4.2m },
            ]);

            int i20 = GetOrCreateModel(hyundaiId3, "i20", "hyundai-i20");
            PrepareGenerations(i20,
                ("GB (2014–2020)", "hyundai-i20-gb", 2014, 2020),
                ("BC3 (2020–)", "hyundai-i20-bc3", 2020, null));
            AddOrReplaceEngines(GetOrFixGeneration(i20, "GB (2014–2020)", "hyundai-i20-gb", 2014, 2020), 70, [
                new EngineVersion { EngineName = "1.2 84 KM", PowerHP = 84, PowerKW = 62, Displacement = 1248, FuelTypeId = ben,
                    TorqueNm = 122, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.3m, TopSpeedKmh = 165, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "1.0 T-GDI 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 998, FuelTypeId = ben,
                    TorqueNm = 172, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.9m, TopSpeedKmh = 185, FuelConsumptionCombined = 4.8m },
                new EngineVersion { EngineName = "1.1 CRDi 75 KM", PowerHP = 75, PowerKW = 55, Displacement = 1120, FuelTypeId = die,
                    TorqueNm = 205, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 15.8m, TopSpeedKmh = 163, FuelConsumptionCombined = 3.7m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(i20, "BC3 (2020–)", "hyundai-i20-bc3", 2020, null), 80, [
                new EngineVersion { EngineName = "1.2 84 KM", PowerHP = 84, PowerKW = 62, Displacement = 1248, FuelTypeId = ben,
                    TorqueNm = 118, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.7m, TopSpeedKmh = 165, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "1.0 T-GDI 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 998, FuelTypeId = mild,
                    TorqueNm = 172, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.7m, TopSpeedKmh = 187, FuelConsumptionCombined = 4.9m },
                new EngineVersion { EngineName = "N 1.6 T-GDI 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 1591, FuelTypeId = ben,
                    TorqueNm = 275, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.7m, TopSpeedKmh = 230, FuelConsumptionCombined = 7.3m },
            ]);

            int santafe = GetOrCreateModel(hyundaiId3, "Santa Fe", "hyundai-santa-fe");
            PrepareGenerations(santafe,
                ("TM (2018–2023)", "hyundai-santa-fe-tm", 2018, 2023),
                ("MX5 (2024–)", "hyundai-santa-fe-mx5", 2024, null));
            AddOrReplaceEngines(GetOrFixGeneration(santafe, "TM (2018–2023)", "hyundai-santa-fe-tm", 2018, 2023), 150, [
                new EngineVersion { EngineName = "2.2 CRDi 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 2151, FuelTypeId = die,
                    TorqueNm = 440, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.3m, TopSpeedKmh = 201, FuelConsumptionCombined = 6.8m },
                new EngineVersion { EngineName = "1.6 T-GDI PHEV 265 KM", PowerHP = 265, PowerKW = 195, Displacement = 1598, FuelTypeId = phev,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.1m, TopSpeedKmh = 187, FuelConsumptionCombined = 1.6m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(santafe, "MX5 (2024–)", "hyundai-santa-fe-mx5", 2024, null), 200, [
                new EngineVersion { EngineName = "1.6 T-GDI Hybrid 215 KM", PowerHP = 215, PowerKW = 158, Displacement = 1598, FuelTypeId = hyb,
                    TorqueNm = 264, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.1m, TopSpeedKmh = 187, FuelConsumptionCombined = 5.8m },
            ]);

            int ioniq5 = GetOrCreateModel(hyundaiId3, "Ioniq 5", "hyundai-ioniq-5");
            AddOrReplaceEngines(GetOrFixGeneration(ioniq5, "I (2021–)", "hyundai-ioniq-5-i", 2021, null), 150, [
                new EngineVersion { EngineName = "58 kWh RWD 170 KM", PowerHP = 170, PowerKW = 125, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "RWD",
                    Cylinders = 0, Acceleration0100 = 8.5m, TopSpeedKmh = 185, FuelConsumptionCombined = 16.8m },
                new EngineVersion { EngineName = "77.4 kWh AWD 305 KM", PowerHP = 305, PowerKW = 225, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 605, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 5.2m, TopSpeedKmh = 185, FuelConsumptionCombined = 19.0m },
                new EngineVersion { EngineName = "N 84 kWh AWD 650 KM", PowerHP = 650, PowerKW = 478, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 740, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 3.4m, TopSpeedKmh = 260, FuelConsumptionCombined = 21.0m },
            ]);

            int bayon = GetOrCreateModel(hyundaiId3, "Bayon", "hyundai-bayon");
            AddOrReplaceEngines(GetOrFixGeneration(bayon, "I (2021–)", "hyundai-bayon-i", 2021, null), 80, [
                new EngineVersion { EngineName = "1.0 T-GDI 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 998, FuelTypeId = mild,
                    TorqueNm = 172, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.4m, TopSpeedKmh = 181, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "1.2 84 KM", PowerHP = 84, PowerKW = 62, Displacement = 1197, FuelTypeId = ben,
                    TorqueNm = 118, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.0m, TopSpeedKmh = 165, FuelConsumptionCombined = 5.9m },
            ]);

            int i10 = GetOrCreateModel(hyundaiId3, "i10", "hyundai-i10");
            PrepareGenerations(i10,
                ("PA (2013–2019)", "hyundai-i10-pa", 2013, 2019),
                ("AC3 (2019–)", "hyundai-i10-ac3", 2019, null));
            AddOrReplaceEngines(GetOrFixGeneration(i10, "PA (2013–2019)", "hyundai-i10-pa", 2013, 2019), 60, [
                new EngineVersion { EngineName = "1.0 66 KM", PowerHP = 66, PowerKW = 49, Displacement = 998, FuelTypeId = ben,
                    TorqueNm = 96, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 14.9m, TopSpeedKmh = 155, FuelConsumptionCombined = 5.0m },
                new EngineVersion { EngineName = "1.2 87 KM", PowerHP = 87, PowerKW = 64, Displacement = 1248, FuelTypeId = ben,
                    TorqueNm = 122, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.3m, TopSpeedKmh = 165, FuelConsumptionCombined = 5.4m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(i10, "AC3 (2019–)", "hyundai-i10-ac3", 2019, null), 65, [
                new EngineVersion { EngineName = "1.0 67 KM", PowerHP = 67, PowerKW = 49, Displacement = 998, FuelTypeId = ben,
                    TorqueNm = 96, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 14.7m, TopSpeedKmh = 155, FuelConsumptionCombined = 5.0m },
                new EngineVersion { EngineName = "1.2 84 KM", PowerHP = 84, PowerKW = 62, Displacement = 1197, FuelTypeId = ben,
                    TorqueNm = 118, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.2m, TopSpeedKmh = 160, FuelConsumptionCombined = 5.3m },
            ]);

            // ── KIA (Rio, Picanto, Niro, Stonic, XCeed, Sorento, EV9 — Ceed/Sportage/EV6 already seeded by ModelSeeder) ──
            int kiaId3 = GetOrCreateBrand("Kia", "kia", "auta-osobowe");
            int rio = GetOrCreateModel(kiaId3, "Rio", "kia-rio");
            PrepareGenerations(rio,
                ("IV (2017–2023)", "kia-rio-iv", 2017, 2023),
                ("V (2023–)", "kia-rio-v", 2023, null));
            AddOrReplaceEngines(GetOrFixGeneration(rio, "IV (2017–2023)", "kia-rio-iv", 2017, 2023), 70, [
                new EngineVersion { EngineName = "1.0 T-GDI 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 998, FuelTypeId = ben,
                    TorqueNm = 172, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.9m, TopSpeedKmh = 185, FuelConsumptionCombined = 4.9m },
                new EngineVersion { EngineName = "1.2 84 KM", PowerHP = 84, PowerKW = 62, Displacement = 1248, FuelTypeId = ben,
                    TorqueNm = 122, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.3m, TopSpeedKmh = 165, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "1.4 CRDi 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1396, FuelTypeId = die,
                    TorqueNm = 220, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.2m, TopSpeedKmh = 175, FuelConsumptionCombined = 4.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(rio, "V (2023–)", "kia-rio-v", 2023, null), 90, [
                new EngineVersion { EngineName = "1.0 T-GDI 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 998, FuelTypeId = mild,
                    TorqueNm = 172, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.7m, TopSpeedKmh = 185, FuelConsumptionCombined = 4.9m },
            ]);

            int picanto = GetOrCreateModel(kiaId3, "Picanto", "kia-picanto");
            AddOrReplaceEngines(GetOrFixGeneration(picanto, "III (2017–)", "kia-picanto-iii", 2017, null), 55, [
                new EngineVersion { EngineName = "1.0 67 KM", PowerHP = 67, PowerKW = 49, Displacement = 998, FuelTypeId = ben,
                    TorqueNm = 96, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 14.8m, TopSpeedKmh = 155, FuelConsumptionCombined = 5.0m },
                new EngineVersion { EngineName = "1.0 T-GDI GT-Line 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 998, FuelTypeId = ben,
                    TorqueNm = 172, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.7m, TopSpeedKmh = 181, FuelConsumptionCombined = 5.1m },
            ]);

            int niro = GetOrCreateModel(kiaId3, "Niro", "kia-niro");
            PrepareGenerations(niro,
                ("I (2016–2022)", "kia-niro-i", 2016, 2022),
                ("II (2022–)", "kia-niro-ii", 2022, null));
            AddOrReplaceEngines(GetOrFixGeneration(niro, "I (2016–2022)", "kia-niro-i", 2016, 2022), 100, [
                new EngineVersion { EngineName = "1.6 GDI HEV 141 KM", PowerHP = 141, PowerKW = 104, Displacement = 1580, FuelTypeId = hyb,
                    TorqueNm = 265, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.8m, TopSpeedKmh = 162, FuelConsumptionCombined = 3.8m },
                new EngineVersion { EngineName = "1.6 GDI PHEV 141 KM", PowerHP = 141, PowerKW = 104, Displacement = 1580, FuelTypeId = phev,
                    TorqueNm = 265, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.8m, TopSpeedKmh = 172, FuelConsumptionCombined = 1.3m },
                new EngineVersion { EngineName = "EV 64 kWh 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 395, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 0, Acceleration0100 = 7.8m, TopSpeedKmh = 167, FuelConsumptionCombined = 15.9m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(niro, "II (2022–)", "kia-niro-ii", 2022, null), 105, [
                new EngineVersion { EngineName = "1.6 GDI HEV 141 KM", PowerHP = 141, PowerKW = 104, Displacement = 1580, FuelTypeId = hyb,
                    TorqueNm = 265, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.4m, TopSpeedKmh = 162, FuelConsumptionCombined = 3.9m },
                new EngineVersion { EngineName = "EV 64.8 kWh 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 255, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 0, Acceleration0100 = 7.8m, TopSpeedKmh = 167, FuelConsumptionCombined = 15.6m },
            ]);

            int stonic = GetOrCreateModel(kiaId3, "Stonic", "kia-stonic");
            AddOrReplaceEngines(GetOrFixGeneration(stonic, "I (2017–)", "kia-stonic-i", 2017, null), 80, [
                new EngineVersion { EngineName = "1.0 T-GDI 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 998, FuelTypeId = mild,
                    TorqueNm = 172, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.1m, TopSpeedKmh = 181, FuelConsumptionCombined = 5.3m },
                new EngineVersion { EngineName = "1.4 CRDi 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1396, FuelTypeId = die,
                    TorqueNm = 240, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.5m, TopSpeedKmh = 174, FuelConsumptionCombined = 4.2m },
            ]);

            int xceed = GetOrCreateModel(kiaId3, "XCeed", "kia-xceed");
            AddOrReplaceEngines(GetOrFixGeneration(xceed, "I (2019–)", "kia-xceed-i", 2019, null), 100, [
                new EngineVersion { EngineName = "1.0 T-GDI 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 998, FuelTypeId = mild,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.4m, TopSpeedKmh = 188, FuelConsumptionCombined = 5.8m },
                new EngineVersion { EngineName = "PHEV 1.6 141 KM", PowerHP = 141, PowerKW = 104, Displacement = 1580, FuelTypeId = phev,
                    TorqueNm = 265, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.6m, TopSpeedKmh = 172, FuelConsumptionCombined = 1.4m },
            ]);

            int sorento = GetOrCreateModel(kiaId3, "Sorento", "kia-sorento");
            PrepareGenerations(sorento,
                ("IV (2020–)", "kia-sorento-iv", 2020, null));
            AddOrReplaceEngines(GetOrFixGeneration(sorento, "IV (2020–)", "kia-sorento-iv", 2020, null), 180, [
                new EngineVersion { EngineName = "2.2 CRDi 202 KM", PowerHP = 202, PowerKW = 148, Displacement = 2151, FuelTypeId = die,
                    TorqueNm = 440, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.0m, TopSpeedKmh = 201, FuelConsumptionCombined = 6.4m },
                new EngineVersion { EngineName = "1.6 T-GDI PHEV 265 KM", PowerHP = 265, PowerKW = 195, Displacement = 1598, FuelTypeId = phev,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.7m, TopSpeedKmh = 193, FuelConsumptionCombined = 1.6m },
            ]);

            int ev9 = GetOrCreateModel(kiaId3, "EV9", "kia-ev9");
            AddOrReplaceEngines(GetOrFixGeneration(ev9, "I (2023–)", "kia-ev9-i", 2023, null), 200, [
                new EngineVersion { EngineName = "76.1 kWh RWD 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "RWD",
                    Cylinders = 0, Acceleration0100 = 9.4m, TopSpeedKmh = 185, FuelConsumptionCombined = 18.9m },
                new EngineVersion { EngineName = "99.8 kWh AWD 385 KM", PowerHP = 385, PowerKW = 283, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 5.3m, TopSpeedKmh = 200, FuelConsumptionCombined = 22.3m },
            ]);

            int opelId = GetOrCreateBrand("Opel", "opel", "auta-osobowe");
            int astra = GetOrCreateModel(opelId, "Astra", "opel-astra");
            AddEngines(GetOrCreateGeneration(astra, "K (2015–2021)", "opel-astra-k", 2015, 2021), [
                new EngineVersion { EngineName = "1.4 Turbo 125 KM", PowerHP = 125, PowerKW = 92, Displacement = 1399, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.0m, TopSpeedKmh = 200, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "1.4 Turbo 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1399, FuelTypeId = ben,
                    TorqueNm = 236, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.7m, TopSpeedKmh = 217, FuelConsumptionCombined = 6.6m },
                new EngineVersion { EngineName = "OPC 280 KM", PowerHP = 280, PowerKW = 206, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.0m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.7m },
                new EngineVersion { EngineName = "1.6 CDTi 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 280, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.2m, TopSpeedKmh = 193, FuelConsumptionCombined = 3.9m },
                new EngineVersion { EngineName = "1.6 CDTi 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.3m, TopSpeedKmh = 212, FuelConsumptionCombined = 4.1m },
            ]);
            AddEngines(GetOrCreateGeneration(astra, "L (2021–)", "opel-astra-l", 2021, null), [
                new EngineVersion { EngineName = "1.2 Turbo 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.5m, TopSpeedKmh = 210, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "1.6 Hybrid 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 1598, FuelTypeId = hyb,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.7m, TopSpeedKmh = 225, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "PHEV 225 KM", PowerHP = 225, PowerKW = 165, Displacement = 1598, FuelTypeId = phev,
                    TorqueNm = 360, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 225, FuelConsumptionCombined = 1.5m },
                new EngineVersion { EngineName = "1.5 Diesel 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 205, FuelConsumptionCombined = 4.4m },
            ]);
        }

        // ── BMW SERIA 3 / SERIA 5 (TorqueNm enrichment) ──────────────────────────
        {
            int bId = GetOrCreateBrand("BMW", "bmw", "auta-osobowe");

            int s3 = GetOrCreateModel(bId, "Seria 3", "bmw-seria-3");
            AddEngines(GetOrCreateGeneration(s3, "F30 (2011–2018)", "bmw-3-f30", 2011, 2018), [
                new EngineVersion { EngineName = "318i 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1499, FuelTypeId = ben,
                    TorqueNm = 220, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 8.8m, TopSpeedKmh = 210, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "320i 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 270, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.0m, TopSpeedKmh = 236, FuelConsumptionCombined = 6.8m },
                new EngineVersion { EngineName = "328i 245 KM", PowerHP = 245, PowerKW = 180, Displacement = 1997, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 5.8m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "M3 431 KM", PowerHP = 431, PowerKW = 317, Displacement = 2979, FuelTypeId = ben,
                    TorqueNm = 550, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 4.1m, TopSpeedKmh = 250, FuelConsumptionCombined = 9.9m },
                new EngineVersion { EngineName = "318d 143 KM", PowerHP = 143, PowerKW = 105, Displacement = 1995, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 9.0m, TopSpeedKmh = 210, FuelConsumptionCombined = 4.5m },
                new EngineVersion { EngineName = "320d 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1995, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 231, FuelConsumptionCombined = 4.8m },
                new EngineVersion { EngineName = "330d 258 KM", PowerHP = 258, PowerKW = 190, Displacement = 2993, FuelTypeId = die,
                    TorqueNm = 560, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 5.5m, TopSpeedKmh = 250, FuelConsumptionCombined = 5.5m },
            ]);
            AddEngines(GetOrCreateGeneration(s3, "G20 (2018–)", "bmw-3-g20", 2018, null), [
                new EngineVersion { EngineName = "318i 156 KM", PowerHP = 156, PowerKW = 115, Displacement = 1499, FuelTypeId = mild,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 215, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "320i 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 1998, FuelTypeId = mild,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.0m, TopSpeedKmh = 235, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "330i 258 KM", PowerHP = 258, PowerKW = 190, Displacement = 1998, FuelTypeId = mild,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 5.8m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "M340i 374 KM", PowerHP = 374, PowerKW = 275, Displacement = 2998, FuelTypeId = mild,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 4.4m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "M3 Competition 510 KM", PowerHP = 510, PowerKW = 375, Displacement = 2993, FuelTypeId = ben,
                    TorqueNm = 650, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 3.9m, TopSpeedKmh = 290, FuelConsumptionCombined = 10.9m },
                new EngineVersion { EngineName = "318d 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1995, FuelTypeId = mild,
                    TorqueNm = 360, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 8.8m, TopSpeedKmh = 215, FuelConsumptionCombined = 4.5m },
                new EngineVersion { EngineName = "320d 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1995, FuelTypeId = mild,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 234, FuelConsumptionCombined = 4.8m },
                new EngineVersion { EngineName = "330d 286 KM", PowerHP = 286, PowerKW = 210, Displacement = 2993, FuelTypeId = mild,
                    TorqueNm = 580, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.5m, TopSpeedKmh = 250, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "330e PHEV 292 KM", PowerHP = 292, PowerKW = 215, Displacement = 1998, FuelTypeId = phev,
                    TorqueNm = 420, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 6.0m, TopSpeedKmh = 225, FuelConsumptionCombined = 1.7m },
            ]);

            int s5 = GetOrCreateModel(bId, "Seria 5", "bmw-seria-5");
            AddEngines(GetOrCreateGeneration(s5, "G30 (2016–)", "bmw-5-g30", 2016, null), [
                new EngineVersion { EngineName = "520i 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 1998, FuelTypeId = mild,
                    TorqueNm = 290, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.8m, TopSpeedKmh = 234, FuelConsumptionCombined = 6.8m },
                new EngineVersion { EngineName = "530i 252 KM", PowerHP = 252, PowerKW = 185, Displacement = 1998, FuelTypeId = mild,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 6.2m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.2m },
                new EngineVersion { EngineName = "540i 333 KM", PowerHP = 333, PowerKW = 245, Displacement = 2998, FuelTypeId = mild,
                    TorqueNm = 450, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 4.9m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.2m },
                new EngineVersion { EngineName = "M5 Competition 625 KM", PowerHP = 625, PowerKW = 460, Displacement = 4395, FuelTypeId = ben,
                    TorqueNm = 750, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 3.3m, TopSpeedKmh = 305, FuelConsumptionCombined = 11.6m },
                new EngineVersion { EngineName = "520d 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1995, FuelTypeId = mild,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 238, FuelConsumptionCombined = 4.8m },
                new EngineVersion { EngineName = "530d 286 KM", PowerHP = 286, PowerKW = 210, Displacement = 2993, FuelTypeId = mild,
                    TorqueNm = 580, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.7m, TopSpeedKmh = 250, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "530e PHEV 292 KM", PowerHP = 292, PowerKW = 215, Displacement = 1998, FuelTypeId = phev,
                    TorqueNm = 420, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 6.2m, TopSpeedKmh = 235, FuelConsumptionCombined = 1.9m },
            ]);
        }

        // ── AUDI A4 B9 (TorqueNm enrichment) ─────────────────────────────────────
        {
            int bId = GetOrCreateBrand("Audi", "audi", "auta-osobowe");

            int a4 = GetOrCreateModel(bId, "A4", "audi-a4");
            AddEngines(GetOrCreateGeneration(a4, "B9 (2015–)", "audi-a4-b9", 2015, null), [
                new EngineVersion { EngineName = "35 TFSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1498, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.6m, TopSpeedKmh = 218, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "40 TFSI 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 1984, FuelTypeId = mild,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.3m, TopSpeedKmh = 237, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "45 TFSI 265 KM", PowerHP = 265, PowerKW = 195, Displacement = 1984, FuelTypeId = mild,
                    TorqueNm = 370, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 5.7m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.8m },
                new EngineVersion { EngineName = "S4 TFSI 341 KM", PowerHP = 341, PowerKW = 251, Displacement = 2995, FuelTypeId = mild,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 4.7m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "30 TDI 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.3m, TopSpeedKmh = 208, FuelConsumptionCombined = 4.4m },
                new EngineVersion { EngineName = "35 TDI 163 KM", PowerHP = 163, PowerKW = 120, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.4m, TopSpeedKmh = 225, FuelConsumptionCombined = 4.6m },
                new EngineVersion { EngineName = "40 TDI 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 237, FuelConsumptionCombined = 5.0m },
            ]);
        }

        // ── MERCEDES-BENZ KLASA C / E (TorqueNm enrichment) ──────────────────────
        {
            int bId = GetOrCreateBrand("Mercedes-Benz", "mercedes-benz", "auta-osobowe");

            int klasaC = GetOrCreateModel(bId, "Klasa C", "mb-klasa-c");
            AddEngines(GetOrCreateGeneration(klasaC, "W205 (2014–2021)", "mb-c-w205", 2014, 2021), [
                new EngineVersion { EngineName = "C180 156 KM", PowerHP = 156, PowerKW = 115, Displacement = 1595, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 218, FuelConsumptionCombined = 6.4m },
                new EngineVersion { EngineName = "C200 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 1991, FuelTypeId = mild,
                    TorqueNm = 300, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.3m, TopSpeedKmh = 235, FuelConsumptionCombined = 6.8m },
                new EngineVersion { EngineName = "C300 258 KM", PowerHP = 258, PowerKW = 190, Displacement = 1991, FuelTypeId = mild,
                    TorqueNm = 370, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 5.8m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "C63 AMG 476 KM", PowerHP = 476, PowerKW = 350, Displacement = 3982, FuelTypeId = ben,
                    TorqueNm = 650, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 4.0m, TopSpeedKmh = 250, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "C200d 160 KM", PowerHP = 160, PowerKW = 118, Displacement = 1598, FuelTypeId = mild,
                    TorqueNm = 360, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 222, FuelConsumptionCombined = 4.3m },
                new EngineVersion { EngineName = "C220d 194 KM", PowerHP = 194, PowerKW = 143, Displacement = 1950, FuelTypeId = mild,
                    TorqueNm = 400, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.6m, TopSpeedKmh = 234, FuelConsumptionCombined = 4.6m },
                new EngineVersion { EngineName = "C300e PHEV 320 KM", PowerHP = 320, PowerKW = 235, Displacement = 1991, FuelTypeId = phev,
                    TorqueNm = 700, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 5.7m, TopSpeedKmh = 250, FuelConsumptionCombined = 1.7m },
            ]);
            AddEngines(GetOrCreateGeneration(klasaC, "W206 (2021–)", "mb-c-w206", 2021, null), [
                new EngineVersion { EngineName = "C180 170 KM", PowerHP = 170, PowerKW = 125, Displacement = 1496, FuelTypeId = mild,
                    TorqueNm = 280, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.8m, TopSpeedKmh = 219, FuelConsumptionCombined = 6.6m },
                new EngineVersion { EngineName = "C200 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 1496, FuelTypeId = mild,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.3m, TopSpeedKmh = 236, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "C300 258 KM", PowerHP = 258, PowerKW = 190, Displacement = 1999, FuelTypeId = mild,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 5.8m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "C220d 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 1993, FuelTypeId = mild,
                    TorqueNm = 440, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 6.9m, TopSpeedKmh = 232, FuelConsumptionCombined = 4.6m },
                new EngineVersion { EngineName = "C300e PHEV 313 KM", PowerHP = 313, PowerKW = 230, Displacement = 1496, FuelTypeId = phev,
                    TorqueNm = 550, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 5.5m, TopSpeedKmh = 250, FuelConsumptionCombined = 1.5m },
            ]);

            int klasaE = GetOrCreateModel(bId, "Klasa E", "mb-klasa-e");
            AddEngines(GetOrCreateGeneration(klasaE, "W213 (2016–2023)", "mb-e-w213", 2016, 2023), [
                new EngineVersion { EngineName = "E200 197 KM", PowerHP = 197, PowerKW = 145, Displacement = 1991, FuelTypeId = mild,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 237, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "E300 258 KM", PowerHP = 258, PowerKW = 190, Displacement = 1991, FuelTypeId = mild,
                    TorqueNm = 370, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 6.1m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.6m },
                new EngineVersion { EngineName = "E63 AMG S 612 KM", PowerHP = 612, PowerKW = 450, Displacement = 3982, FuelTypeId = ben,
                    TorqueNm = 850, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 3.4m, TopSpeedKmh = 300, FuelConsumptionCombined = 11.3m },
                new EngineVersion { EngineName = "E220d 194 KM", PowerHP = 194, PowerKW = 143, Displacement = 1950, FuelTypeId = mild,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.3m, TopSpeedKmh = 240, FuelConsumptionCombined = 5.0m },
                new EngineVersion { EngineName = "E400d 340 KM", PowerHP = 340, PowerKW = 250, Displacement = 2925, FuelTypeId = mild,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.2m, TopSpeedKmh = 250, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "E300e PHEV 320 KM", PowerHP = 320, PowerKW = 235, Displacement = 1991, FuelTypeId = phev,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 5.7m, TopSpeedKmh = 250, FuelConsumptionCombined = 1.7m },
            ]);
        }

        // ── TOYOTA COROLLA / HYUNDAI i30 / KIA CEED / RENAULT MEGANE ────────────
        {
            int toyotaId = GetOrCreateBrand("Toyota", "toyota", "auta-osobowe");
            int corolla = GetOrCreateModel(toyotaId, "Corolla", "toyota-corolla");
            AddEngines(GetOrCreateGeneration(corolla, "E210 (2018–)", "toyota-corolla-e210", 2018, null), [
                new EngineVersion { EngineName = "1.2 T 116 KM", PowerHP = 116, PowerKW = 85, Displacement = 1197, FuelTypeId = ben,
                    TorqueNm = 185, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.2m, TopSpeedKmh = 193, FuelConsumptionCombined = 6.3m },
                new EngineVersion { EngineName = "2.0 GR Sport 261 KM", PowerHP = 261, PowerKW = 192, Displacement = 1987, FuelTypeId = ben,
                    TorqueNm = 360, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.5m, TopSpeedKmh = 235, FuelConsumptionCombined = 9.5m },
                new EngineVersion { EngineName = "1.8 HEV 122 KM", PowerHP = 122, PowerKW = 90, Displacement = 1798, FuelTypeId = hyb,
                    TorqueNm = 163, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 180, FuelConsumptionCombined = 4.3m },
                new EngineVersion { EngineName = "2.0 HEV 196 KM", PowerHP = 196, PowerKW = 144, Displacement = 1987, FuelTypeId = hyb,
                    TorqueNm = 190, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.5m, TopSpeedKmh = 200, FuelConsumptionCombined = 4.8m },
            ]);

            int hyundaiId = GetOrCreateBrand("Hyundai", "hyundai", "auta-osobowe");
            int i30 = GetOrCreateModel(hyundaiId, "i30", "hyundai-i30");
            AddEngines(GetOrCreateGeneration(i30, "PD (2016–)", "hyundai-i30-pd", 2016, null), [
                new EngineVersion { EngineName = "1.0 T-GDI 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 998, FuelTypeId = ben,
                    TorqueNm = 172, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.0m, TopSpeedKmh = 189, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "1.4 T-GDI 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1353, FuelTypeId = ben,
                    TorqueNm = 242, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.0m, TopSpeedKmh = 205, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "N 275 KM", PowerHP = 275, PowerKW = 202, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 353, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.1m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.1m },
                new EngineVersion { EngineName = "1.6 CRDi 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1582, FuelTypeId = die,
                    TorqueNm = 280, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 195, FuelConsumptionCombined = 4.2m },
            ]);

            int kiaId = GetOrCreateBrand("Kia", "kia", "auta-osobowe");
            int ceed = GetOrCreateModel(kiaId, "Ceed", "kia-ceed");
            AddEngines(GetOrCreateGeneration(ceed, "III (2018–)", "kia-ceed-iii", 2018, null), [
                new EngineVersion { EngineName = "1.0 T-GDI 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 998, FuelTypeId = mild,
                    TorqueNm = 172, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.2m, TopSpeedKmh = 188, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "1.4 T-GDI 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1353, FuelTypeId = mild,
                    TorqueNm = 242, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.0m, TopSpeedKmh = 205, FuelConsumptionCombined = 6.4m },
                new EngineVersion { EngineName = "GT 1.6 T-GDI 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 1591, FuelTypeId = ben,
                    TorqueNm = 265, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.0m, TopSpeedKmh = 230, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "1.6 CRDi 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 280, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.8m, TopSpeedKmh = 193, FuelConsumptionCombined = 4.1m },
            ]);

            int renaultId = GetOrCreateBrand("Renault", "renault", "auta-osobowe");
            int megane = GetOrCreateModel(renaultId, "Megane", "renault-megane");
            AddEngines(GetOrCreateGeneration(megane, "IV (2015–)", "renault-megane-iv", 2015, null), [
                new EngineVersion { EngineName = "1.3 TCe 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1332, FuelTypeId = ben,
                    TorqueNm = 260, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.8m, TopSpeedKmh = 209, FuelConsumptionCombined = 6.3m },
                new EngineVersion { EngineName = "1.8 TCe RS 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 1798, FuelTypeId = ben,
                    TorqueNm = 390, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 5.7m, TopSpeedKmh = 260, FuelConsumptionCombined = 8.4m },
                new EngineVersion { EngineName = "1.5 dCi 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 260, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.2m, TopSpeedKmh = 204, FuelConsumptionCombined = 4.0m },
                new EngineVersion { EngineName = "E-Tech PHEV 160 KM", PowerHP = 160, PowerKW = 118, Displacement = 1618, FuelTypeId = phev,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 172, FuelConsumptionCombined = 1.4m },
            ]);

            // ── VW POLO AW ───────────────────────────────────────────────────────────
            int volkswagenId2 = GetOrCreateBrand("Volkswagen", "volkswagen", "auta-osobowe");
            int polo = GetOrCreateModel(volkswagenId2, "Polo", "vw-polo");
            AddEngines(GetOrCreateGeneration(polo, "AW (2017–)", "vw-polo-aw", 2017, null), [
                new EngineVersion { EngineName = "1.0 MPI 65 KM", PowerHP = 65, PowerKW = 48, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 95, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 15.8m, TopSpeedKmh = 166, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "1.0 TSI 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 175, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.0m, TopSpeedKmh = 187, FuelConsumptionCombined = 5.3m },
                new EngineVersion { EngineName = "1.0 TSI 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.5m, TopSpeedKmh = 200, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "2.0 TSI GTI 207 KM", PowerHP = 207, PowerKW = 152, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.5m, TopSpeedKmh = 237, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "1.6 TDI 80 KM", PowerHP = 80, PowerKW = 59, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 195, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.2m, TopSpeedKmh = 180, FuelConsumptionCombined = 4.3m },
                new EngineVersion { EngineName = "1.6 TDI 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.2m, TopSpeedKmh = 192, FuelConsumptionCombined = 4.3m },
            ]);

            // ── AUDI A3 8V + 8Y ──────────────────────────────────────────────────────
            int audiId2 = GetOrCreateBrand("Audi", "audi", "auta-osobowe");
            int a3 = GetOrCreateModel(audiId2, "A3", "audi-a3");
            AddEngines(GetOrCreateGeneration(a3, "8V (2012–2020)", "audi-a3-8v", 2012, 2020), [
                new EngineVersion { EngineName = "1.0 TFSI 116 KM", PowerHP = 116, PowerKW = 85, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.9m, TopSpeedKmh = 204, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "1.4 TFSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1395, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.2m, TopSpeedKmh = 224, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "2.0 TFSI 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.2m, TopSpeedKmh = 238, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "S3 2.0 TFSI 310 KM", PowerHP = 310, PowerKW = 228, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 4.7m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "1.6 TDI 116 KM", PowerHP = 116, PowerKW = 85, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.1m, TopSpeedKmh = 210, FuelConsumptionCombined = 4.0m },
                new EngineVersion { EngineName = "2.0 TDI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 227, FuelConsumptionCombined = 4.4m },
            ]);
            AddEngines(GetOrCreateGeneration(a3, "8Y (2020–)", "audi-a3-8y", 2020, null), [
                new EngineVersion { EngineName = "30 TFSI 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 999, FuelTypeId = mild,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.4m, TopSpeedKmh = 200, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "35 TFSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1498, FuelTypeId = mild,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.4m, TopSpeedKmh = 225, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "S3 40 TFSI 310 KM", PowerHP = 310, PowerKW = 228, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 4.7m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.6m },
                new EngineVersion { EngineName = "30 TDI 116 KM", PowerHP = 116, PowerKW = 85, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.0m, TopSpeedKmh = 207, FuelConsumptionCombined = 4.0m },
                new EngineVersion { EngineName = "35 TDI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 360, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.9m, TopSpeedKmh = 225, FuelConsumptionCombined = 4.1m },
                new EngineVersion { EngineName = "45 TFSIe PHEV 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 1395, FuelTypeId = phev,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.6m, TopSpeedKmh = 225, FuelConsumptionCombined = 1.3m },
            ]);

            // ── SEAT LEON 5F + KL8 ───────────────────────────────────────────────────
            int seatId = GetOrCreateBrand("Seat", "seat", "auta-osobowe");
            int leon = GetOrCreateModel(seatId, "Leon", "seat-leon");
            AddEngines(GetOrCreateGeneration(leon, "5F (2012–2019)", "seat-leon-5f", 2012, 2019), [
                new EngineVersion { EngineName = "1.0 TSI 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.8m, TopSpeedKmh = 203, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "1.4 TSI 125 KM", PowerHP = 125, PowerKW = 92, Displacement = 1395, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.2m, TopSpeedKmh = 211, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "1.5 TSI 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1498, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.1m, TopSpeedKmh = 215, FuelConsumptionCombined = 5.7m },
                new EngineVersion { EngineName = "Cupra 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 380, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 5.7m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "1.6 TDI 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.6m, TopSpeedKmh = 208, FuelConsumptionCombined = 4.2m },
                new EngineVersion { EngineName = "2.0 TDI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 340, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.8m, TopSpeedKmh = 220, FuelConsumptionCombined = 4.6m },
            ]);
            AddEngines(GetOrCreateGeneration(leon, "KL8 (2020–)", "seat-leon-kl8", 2020, null), [
                new EngineVersion { EngineName = "1.0 eTSI 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 999, FuelTypeId = mild,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.0m, TopSpeedKmh = 197, FuelConsumptionCombined = 5.3m },
                new EngineVersion { EngineName = "1.5 TSI 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1498, FuelTypeId = mild,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.7m, TopSpeedKmh = 213, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "2.0 TSI FR 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1984, FuelTypeId = mild,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.0m, TopSpeedKmh = 237, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "2.0 TDI 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.6m, TopSpeedKmh = 207, FuelConsumptionCombined = 4.1m },
                new EngineVersion { EngineName = "2.0 TDI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 360, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.8m, TopSpeedKmh = 218, FuelConsumptionCombined = 4.3m },
                new EngineVersion { EngineName = "e-Hybrid PHEV 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 1395, FuelTypeId = phev,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.4m, TopSpeedKmh = 225, FuelConsumptionCombined = 1.4m },
            ]);

            int arona = GetOrCreateModel(seatId, "Arona", "seat-arona");
            AddOrReplaceEngines(GetOrFixGeneration(arona, "KJ7 (2017–)", "seat-arona-kj7", 2017, null), 90, [
                new EngineVersion { EngineName = "1.0 TSI 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 175, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.4m, TopSpeedKmh = 173, FuelConsumptionCombined = 5.3m },
                new EngineVersion { EngineName = "1.0 TSI 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.7m, TopSpeedKmh = 191, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "FR 1.5 TSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1498, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 202, FuelConsumptionCombined = 5.8m },
            ]);

            int tarraco = GetOrCreateModel(seatId, "Tarraco", "seat-tarraco");
            AddOrReplaceEngines(GetOrFixGeneration(tarraco, "KH7 (2018–)", "seat-tarraco-kh7", 2018, null), 130, [
                new EngineVersion { EngineName = "1.5 TSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1498, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.7m, TopSpeedKmh = 199, FuelConsumptionCombined = 6.9m },
                new EngineVersion { EngineName = "2.0 TSI 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.9m, TopSpeedKmh = 210, FuelConsumptionCombined = 8.1m },
                new EngineVersion { EngineName = "2.0 TDI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 340, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 199, FuelConsumptionCombined = 5.7m },
            ]);

            int alhambra = GetOrCreateModel(seatId, "Alhambra", "seat-alhambra");
            AddOrReplaceEngines(GetOrFixGeneration(alhambra, "II (2010–2020)", "seat-alhambra-ii", 2010, 2020), 130, [
                new EngineVersion { EngineName = "1.4 TSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1395, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.9m, TopSpeedKmh = 190, FuelConsumptionCombined = 7.6m },
                new EngineVersion { EngineName = "2.0 TDI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 340, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 192, FuelConsumptionCombined = 5.6m },
            ]);

            // ── FIAT TIPO 356 ────────────────────────────────────────────────────────
            int fiatId = GetOrCreateBrand("Fiat", "fiat", "auta-osobowe");
            int tipo = GetOrCreateModel(fiatId, "Tipo", "fiat-tipo");
            AddEngines(GetOrCreateGeneration(tipo, "356 (2015–)", "fiat-tipo-356", 2015, null), [
                new EngineVersion { EngineName = "1.0 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 190, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.5m, TopSpeedKmh = 183, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "1.4 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 127, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.0m, TopSpeedKmh = 185, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "1.4 Turbo 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 215, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 206, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "1.3 MultiJet 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1248, FuelTypeId = die,
                    TorqueNm = 200, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 182, FuelConsumptionCombined = 3.9m },
                new EngineVersion { EngineName = "1.6 MultiJet 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 205, FuelConsumptionCombined = 4.2m },
            ]);

            int f124spider = GetOrCreateModel(fiatId, "124 Spider", "fiat-124-spider");
            AddOrReplaceEngines(GetOrFixGeneration(f124spider, "II (2016–2019)", "fiat-124-spider-ii", 2016, 2019), 100, [
                new EngineVersion { EngineName = "1.4 MultiAir Turbo 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 240, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 215, FuelConsumptionCombined = 6.4m },
            ]);

            int f500 = GetOrCreateModel(fiatId, "500", "fiat-500");
            PrepareGenerations(f500,
                ("II (2015–2020)", "fiat-500-ii", 2015, 2020),
                ("III (2020–)", "fiat-500-iii", 2020, null));
            AddOrReplaceEngines(GetOrFixGeneration(f500, "II (2015–2020)", "fiat-500-ii", 2015, 2020), 60, [
                new EngineVersion { EngineName = "1.2 69 KM", PowerHP = 69, PowerKW = 51, Displacement = 1242, FuelTypeId = ben,
                    TorqueNm = 102, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.0m, TopSpeedKmh = 160, FuelConsumptionCombined = 5.0m },
                new EngineVersion { EngineName = "0.9 TwinAir 85 KM", PowerHP = 85, PowerKW = 63, Displacement = 875, FuelTypeId = ben,
                    TorqueNm = 145, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 2, Acceleration0100 = 11.0m, TopSpeedKmh = 173, FuelConsumptionCombined = 4.2m },
                new EngineVersion { EngineName = "1.3 MultiJet 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 1248, FuelTypeId = die,
                    TorqueNm = 200, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.3m, TopSpeedKmh = 165, FuelConsumptionCombined = 3.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(f500, "III (2020–)", "fiat-500-iii", 2020, null), 60, [
                new EngineVersion { EngineName = "500e 70 KM", PowerHP = 70, PowerKW = 51, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 220, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = null, Acceleration0100 = 14.0m, TopSpeedKmh = 135, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "500e 118 KM", PowerHP = 118, PowerKW = 87, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 220, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = null, Acceleration0100 = 9.0m, TopSpeedKmh = 150, FuelConsumptionCombined = null },
            ]);

            int f500l = GetOrCreateModel(fiatId, "500L", "fiat-500l");
            AddOrReplaceEngines(GetOrFixGeneration(f500l, "I (2012–2020)", "fiat-500l-i", 2012, 2020), 70, [
                new EngineVersion { EngineName = "1.4 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 127, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.0m, TopSpeedKmh = 173, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "0.9 TwinAir 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 875, FuelTypeId = ben,
                    TorqueNm = 145, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 2, Acceleration0100 = 11.2m, TopSpeedKmh = 178, FuelConsumptionCombined = 5.0m },
                new EngineVersion { EngineName = "1.3 MultiJet 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 1248, FuelTypeId = die,
                    TorqueNm = 200, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.4m, TopSpeedKmh = 165, FuelConsumptionCombined = 4.3m },
                new EngineVersion { EngineName = "1.6 MultiJet 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 175, FuelConsumptionCombined = 4.5m },
            ]);

            int f500x = GetOrCreateModel(fiatId, "500X", "fiat-500x");
            AddOrReplaceEngines(GetOrFixGeneration(f500x, "I (2015–)", "fiat-500x-i", 2015, null), 100, [
                new EngineVersion { EngineName = "1.0 FireFly 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 190, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.8m, TopSpeedKmh = 182, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "1.3 FireFly Turbo 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1332, FuelTypeId = ben,
                    TorqueNm = 270, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.9m, TopSpeedKmh = 195, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "1.6 MultiJet 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 190, FuelConsumptionCombined = 4.8m },
                new EngineVersion { EngineName = "2.0 MultiJet 4x4 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1956, FuelTypeId = die,
                    TorqueNm = 350, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 190, FuelConsumptionCombined = 5.5m },
            ]);

            int f600 = GetOrCreateModel(fiatId, "600", "fiat-600");
            AddOrReplaceEngines(GetOrFixGeneration(f600, "II (2023–)", "fiat-600-ii", 2023, null), 90, [
                new EngineVersion { EngineName = "1.2 Hybrid 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1199, FuelTypeId = hyb,
                    TorqueNm = 205, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 12.0m, TopSpeedKmh = 172, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "La Prima Electric 156 KM", PowerHP = 156, PowerKW = 115, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 260, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = null, Acceleration0100 = 9.0m, TopSpeedKmh = 150, FuelConsumptionCombined = null },
            ]);

            int bravo = GetOrCreateModel(fiatId, "Bravo", "fiat-bravo");
            PrepareGenerations(bravo,
                ("I (1995–2001)", "fiat-bravo-i", 1995, 2001),
                ("II (2007–2014)", "fiat-bravo-ii", 2007, 2014));
            AddOrReplaceEngines(GetOrFixGeneration(bravo, "I (1995–2001)", "fiat-bravo-i", 1995, 2001), 70, [
                new EngineVersion { EngineName = "1.4 80 KM", PowerHP = 80, PowerKW = 59, Displacement = 1370, FuelTypeId = ben,
                    TorqueNm = 113, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.5m, TopSpeedKmh = 168, FuelConsumptionCombined = 7.2m },
                new EngineVersion { EngineName = "1.6 103 KM", PowerHP = 103, PowerKW = 76, Displacement = 1581, FuelTypeId = ben,
                    TorqueNm = 137, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 185, FuelConsumptionCombined = 7.8m },
                new EngineVersion { EngineName = "1.8 113 KM", PowerHP = 113, PowerKW = 83, Displacement = 1747, FuelTypeId = ben,
                    TorqueNm = 152, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.0m, TopSpeedKmh = 192, FuelConsumptionCombined = 8.3m },
                new EngineVersion { EngineName = "1.9 TD 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1929, FuelTypeId = die,
                    TorqueNm = 200, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 178, FuelConsumptionCombined = 5.7m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(bravo, "II (2007–2014)", "fiat-bravo-ii", 2007, 2014), 80, [
                new EngineVersion { EngineName = "1.4 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 127, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.5m, TopSpeedKmh = 178, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "1.4 T-Jet 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 206, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.2m, TopSpeedKmh = 207, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "1.6 MultiJet 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 280, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 187, FuelConsumptionCombined = 4.8m },
                new EngineVersion { EngineName = "1.9 MultiJet 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1910, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.2m, TopSpeedKmh = 202, FuelConsumptionCombined = 5.5m },
            ]);

            int doblo = GetOrCreateModel(fiatId, "Doblo", "fiat-doblo");
            PrepareGenerations(doblo,
                ("I (2000–2010)", "fiat-doblo-i", 2000, 2010),
                ("II (2010–2022)", "fiat-doblo-ii", 2010, 2022),
                ("III (2022–)", "fiat-doblo-iii", 2022, null));
            AddOrReplaceEngines(GetOrFixGeneration(doblo, "I (2000–2010)", "fiat-doblo-i", 2000, 2010), 55, [
                new EngineVersion { EngineName = "1.6 103 KM", PowerHP = 103, PowerKW = 76, Displacement = 1596, FuelTypeId = ben,
                    TorqueNm = 137, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.8m, TopSpeedKmh = 165, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "1.9D 63 KM", PowerHP = 63, PowerKW = 46, Displacement = 1910, FuelTypeId = die,
                    TorqueNm = 127, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 18.0m, TopSpeedKmh = 140, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "1.9 JTD 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1910, FuelTypeId = die,
                    TorqueNm = 200, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.5m, TopSpeedKmh = 160, FuelConsumptionCombined = 6.2m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(doblo, "II (2010–2022)", "fiat-doblo-ii", 2010, 2022), 70, [
                new EngineVersion { EngineName = "1.4 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 127, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.0m, TopSpeedKmh = 165, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "1.6 MultiJet 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 280, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.5m, TopSpeedKmh = 170, FuelConsumptionCombined = 5.0m },
                new EngineVersion { EngineName = "2.0 MultiJet 135 KM", PowerHP = 135, PowerKW = 99, Displacement = 1956, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 180, FuelConsumptionCombined = 5.7m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(doblo, "III (2022–)", "fiat-doblo-iii", 2022, null), 90, [
                new EngineVersion { EngineName = "1.5 BlueHDi 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.5m, TopSpeedKmh = 168, FuelConsumptionCombined = 4.8m },
                new EngineVersion { EngineName = "1.5 BlueHDi 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 178, FuelConsumptionCombined = 5.0m },
            ]);

            int ducato = GetOrCreateModel(fiatId, "Ducato", "fiat-ducato");
            PrepareGenerations(ducato,
                ("II (2006–2014)", "fiat-ducato-ii", 2006, 2014),
                ("III (2014–)", "fiat-ducato-iii", 2014, null));
            AddOrReplaceEngines(GetOrFixGeneration(ducato, "II (2006–2014)", "fiat-ducato-ii", 2006, 2014), 100, [
                new EngineVersion { EngineName = "2.3 MultiJet 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 2287, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 15.5m, TopSpeedKmh = 155, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "3.0 MultiJet 157 KM", PowerHP = 157, PowerKW = 115, Displacement = 2999, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.0m, TopSpeedKmh = 175, FuelConsumptionCombined = 9.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(ducato, "III (2014–)", "fiat-ducato-iii", 2014, null), 120, [
                new EngineVersion { EngineName = "2.3 MultiJet 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 2287, FuelTypeId = die,
                    TorqueNm = 350, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.5m, TopSpeedKmh = 158, FuelConsumptionCombined = 7.8m },
                new EngineVersion { EngineName = "2.3 MultiJet 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 2287, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 168, FuelConsumptionCombined = 8.2m },
            ]);

            int idea = GetOrCreateModel(fiatId, "Idea", "fiat-idea");
            AddOrReplaceEngines(GetOrFixGeneration(idea, "I (2003–2012)", "fiat-idea-i", 2003, 2012), 55, [
                new EngineVersion { EngineName = "1.2 65 KM", PowerHP = 65, PowerKW = 48, Displacement = 1242, FuelTypeId = ben,
                    TorqueNm = 102, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 16.5m, TopSpeedKmh = 155, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "1.4 77 KM", PowerHP = 77, PowerKW = 57, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 115, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.5m, TopSpeedKmh = 165, FuelConsumptionCombined = 6.6m },
                new EngineVersion { EngineName = "1.3 MultiJet 70 KM", PowerHP = 70, PowerKW = 51, Displacement = 1248, FuelTypeId = die,
                    TorqueNm = 145, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 15.5m, TopSpeedKmh = 155, FuelConsumptionCombined = 4.6m },
                new EngineVersion { EngineName = "1.9 MultiJet 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 1910, FuelTypeId = die,
                    TorqueNm = 260, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.9m, TopSpeedKmh = 180, FuelConsumptionCombined = 5.2m },
            ]);

            int multipla = GetOrCreateModel(fiatId, "Multipla", "fiat-multipla");
            AddOrReplaceEngines(GetOrFixGeneration(multipla, "I (1998–2010)", "fiat-multipla-i", 1998, 2010), 70, [
                new EngineVersion { EngineName = "1.6 103 KM", PowerHP = 103, PowerKW = 76, Displacement = 1581, FuelTypeId = ben,
                    TorqueNm = 143, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.0m, TopSpeedKmh = 165, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "1.9 JTD 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 1910, FuelTypeId = die,
                    TorqueNm = 255, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.5m, TopSpeedKmh = 165, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "1.9 JTD 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1910, FuelTypeId = die,
                    TorqueNm = 280, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.3m, TopSpeedKmh = 172, FuelConsumptionCombined = 6.2m },
            ]);

            int panda = GetOrCreateModel(fiatId, "Panda", "fiat-panda");
            PrepareGenerations(panda,
                ("I (1980–2003)", "fiat-panda-i", 1980, 2003),
                ("II (2003–2012)", "fiat-panda-ii", 2003, 2012),
                ("III (2012–)", "fiat-panda-iii", 2012, null));
            AddOrReplaceEngines(GetOrFixGeneration(panda, "I (1980–2003)", "fiat-panda-i", 1980, 2003), 30, [
                new EngineVersion { EngineName = "900 45 KM", PowerHP = 45, PowerKW = 33, Displacement = 903, FuelTypeId = ben,
                    TorqueNm = 65, EuroNorm = "Euro 1", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 18.0m, TopSpeedKmh = 140, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "1.1 50 KM", PowerHP = 50, PowerKW = 37, Displacement = 1108, FuelTypeId = ben,
                    TorqueNm = 84, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 16.0m, TopSpeedKmh = 145, FuelConsumptionCombined = 6.3m },
                new EngineVersion { EngineName = "1.3D 45 KM", PowerHP = 45, PowerKW = 33, Displacement = 1301, FuelTypeId = die,
                    TorqueNm = 88, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 21.0m, TopSpeedKmh = 132, FuelConsumptionCombined = 4.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(panda, "II (2003–2012)", "fiat-panda-ii", 2003, 2012), 40, [
                new EngineVersion { EngineName = "1.1 54 KM", PowerHP = 54, PowerKW = 40, Displacement = 1108, FuelTypeId = ben,
                    TorqueNm = 88, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 16.4m, TopSpeedKmh = 148, FuelConsumptionCombined = 5.8m },
                new EngineVersion { EngineName = "1.2 69 KM", PowerHP = 69, PowerKW = 51, Displacement = 1242, FuelTypeId = ben,
                    TorqueNm = 102, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.5m, TopSpeedKmh = 157, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "1.3 MultiJet 70 KM", PowerHP = 70, PowerKW = 51, Displacement = 1248, FuelTypeId = die,
                    TorqueNm = 145, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.9m, TopSpeedKmh = 155, FuelConsumptionCombined = 4.2m },
                new EngineVersion { EngineName = "1.3 MultiJet 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1248, FuelTypeId = die,
                    TorqueNm = 200, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 12.8m, TopSpeedKmh = 165, FuelConsumptionCombined = 4.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(panda, "III (2012–)", "fiat-panda-iii", 2012, null), 50, [
                new EngineVersion { EngineName = "1.2 69 KM", PowerHP = 69, PowerKW = 51, Displacement = 1242, FuelTypeId = ben,
                    TorqueNm = 102, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.5m, TopSpeedKmh = 160, FuelConsumptionCombined = 5.2m },
                new EngineVersion { EngineName = "0.9 TwinAir 85 KM", PowerHP = 85, PowerKW = 63, Displacement = 875, FuelTypeId = ben,
                    TorqueNm = 145, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 2, Acceleration0100 = 11.2m, TopSpeedKmh = 173, FuelConsumptionCombined = 4.5m },
                new EngineVersion { EngineName = "1.3 MultiJet 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 1248, FuelTypeId = die,
                    TorqueNm = 200, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.5m, TopSpeedKmh = 168, FuelConsumptionCombined = 4.0m },
            ]);

            int punto = GetOrCreateModel(fiatId, "Punto", "fiat-punto");
            AddOrReplaceEngines(GetOrFixGeneration(punto, "I (1993–1999)", "fiat-punto-i", 1993, 1999), 45, [
                new EngineVersion { EngineName = "1.1 54 KM", PowerHP = 54, PowerKW = 40, Displacement = 1108, FuelTypeId = ben,
                    TorqueNm = 84, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 16.0m, TopSpeedKmh = 150, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "1.2 60 KM", PowerHP = 60, PowerKW = 44, Displacement = 1242, FuelTypeId = ben,
                    TorqueNm = 100, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.0m, TopSpeedKmh = 160, FuelConsumptionCombined = 6.3m },
                new EngineVersion { EngineName = "1.4 GT Turbo 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1372, FuelTypeId = ben,
                    TorqueNm = 186, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.1m, TopSpeedKmh = 195, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "1.7 TD 71 KM", PowerHP = 71, PowerKW = 52, Displacement = 1698, FuelTypeId = die,
                    TorqueNm = 137, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 15.5m, TopSpeedKmh = 158, FuelConsumptionCombined = 5.0m },
            ]);

            int qubo = GetOrCreateModel(fiatId, "Qubo", "fiat-qubo");
            AddOrReplaceEngines(GetOrFixGeneration(qubo, "I (2008–2021)", "fiat-qubo-i", 2008, 2021), 55, [
                new EngineVersion { EngineName = "1.4 77 KM", PowerHP = 77, PowerKW = 57, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 118, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.5m, TopSpeedKmh = 158, FuelConsumptionCombined = 6.4m },
                new EngineVersion { EngineName = "1.3 MultiJet 75 KM", PowerHP = 75, PowerKW = 55, Displacement = 1248, FuelTypeId = die,
                    TorqueNm = 190, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 15.0m, TopSpeedKmh = 155, FuelConsumptionCombined = 4.5m },
                new EngineVersion { EngineName = "1.3 MultiJet 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 1248, FuelTypeId = die,
                    TorqueNm = 200, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.8m, TopSpeedKmh = 165, FuelConsumptionCombined = 4.7m },
            ]);

            int seicento = GetOrCreateModel(fiatId, "Seicento", "fiat-seicento");
            AddOrReplaceEngines(GetOrFixGeneration(seicento, "I (1998–2010)", "fiat-seicento-i", 1998, 2010), 35, [
                new EngineVersion { EngineName = "900 41 KM", PowerHP = 41, PowerKW = 30, Displacement = 899, FuelTypeId = ben,
                    TorqueNm = 64, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 18.5m, TopSpeedKmh = 137, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "1.1 54 KM", PowerHP = 54, PowerKW = 40, Displacement = 1108, FuelTypeId = ben,
                    TorqueNm = 88, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.5m, TopSpeedKmh = 150, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "1.1 Sporting 60 KM", PowerHP = 60, PowerKW = 44, Displacement = 1108, FuelTypeId = ben,
                    TorqueNm = 93, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.5m, TopSpeedKmh = 158, FuelConsumptionCombined = 6.1m },
            ]);

            int stilo = GetOrCreateModel(fiatId, "Stilo", "fiat-stilo");
            AddOrReplaceEngines(GetOrFixGeneration(stilo, "I (2001–2010)", "fiat-stilo-i", 2001, 2010), 70, [
                new EngineVersion { EngineName = "1.4 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 122, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.0m, TopSpeedKmh = 175, FuelConsumptionCombined = 7.2m },
                new EngineVersion { EngineName = "1.6 103 KM", PowerHP = 103, PowerKW = 76, Displacement = 1596, FuelTypeId = ben,
                    TorqueNm = 145, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 185, FuelConsumptionCombined = 7.9m },
                new EngineVersion { EngineName = "1.8 133 KM", PowerHP = 133, PowerKW = 98, Displacement = 1747, FuelTypeId = ben,
                    TorqueNm = 165, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 197, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "1.9 JTD 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1910, FuelTypeId = die,
                    TorqueNm = 255, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.2m, TopSpeedKmh = 188, FuelConsumptionCombined = 5.8m },
                new EngineVersion { EngineName = "1.9 JTD 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1910, FuelTypeId = die,
                    TorqueNm = 305, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.7m, TopSpeedKmh = 197, FuelConsumptionCombined = 6.0m },
            ]);

            int uno = GetOrCreateModel(fiatId, "Uno", "fiat-uno");
            PrepareGenerations(uno,
                ("I (1983–1995)", "fiat-uno-i", 1983, 1995),
                ("II (2010–2021)", "fiat-uno-ii", 2010, 2021));
            AddOrReplaceEngines(GetOrFixGeneration(uno, "I (1983–1995)", "fiat-uno-i", 1983, 1995), 30, [
                new EngineVersion { EngineName = "900 45 KM", PowerHP = 45, PowerKW = 33, Displacement = 903, FuelTypeId = ben,
                    TorqueNm = 65, EuroNorm = "Euro 1", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 18.5m, TopSpeedKmh = 140, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "1.1 55 KM", PowerHP = 55, PowerKW = 40, Displacement = 1108, FuelTypeId = ben,
                    TorqueNm = 85, EuroNorm = "Euro 1", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 15.5m, TopSpeedKmh = 150, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "1.4 Turbo i.e. 118 KM", PowerHP = 118, PowerKW = 87, Displacement = 1372, FuelTypeId = ben,
                    TorqueNm = 190, EuroNorm = "Euro 1", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.6m, TopSpeedKmh = 195, FuelConsumptionCombined = 8.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(uno, "II (2010–2021)", "fiat-uno-ii", 2010, 2021), 55, [
                new EngineVersion { EngineName = "1.0 Fire 75 KM", PowerHP = 75, PowerKW = 55, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 91, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.5m, TopSpeedKmh = 155, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "1.4 Fire 88 KM", PowerHP = 88, PowerKW = 65, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 122, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.0m, TopSpeedKmh = 165, FuelConsumptionCombined = 7.5m },
            ]);

            // ── NISSAN QASHQAI J11 + J12 ─────────────────────────────────────────────
            int nissanId = GetOrCreateBrand("Nissan", "nissan", "auta-osobowe");
            int qashqai = GetOrCreateModel(nissanId, "Qashqai", "nissan-qashqai");
            AddEngines(GetOrCreateGeneration(qashqai, "J11 (2013–2021)", "nissan-qashqai-j11", 2013, 2021), [
                new EngineVersion { EngineName = "1.2 DIG-T 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1197, FuelTypeId = ben,
                    TorqueNm = 190, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.6m, TopSpeedKmh = 183, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "1.6 DIG-T 163 KM", PowerHP = 163, PowerKW = 120, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 240, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.4m, TopSpeedKmh = 205, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "1.5 dCi 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 260, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.6m, TopSpeedKmh = 183, FuelConsumptionCombined = 4.3m },
                new EngineVersion { EngineName = "1.6 dCi 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.2m, TopSpeedKmh = 192, FuelConsumptionCombined = 4.9m },
            ]);
            AddEngines(GetOrCreateGeneration(qashqai, "J12 (2021–)", "nissan-qashqai-j12", 2021, null), [
                new EngineVersion { EngineName = "1.3 DIG-T 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1332, FuelTypeId = mild,
                    TorqueNm = 240, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 197, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "1.3 DIG-T 158 KM", PowerHP = 158, PowerKW = 116, Displacement = 1332, FuelTypeId = mild,
                    TorqueNm = 270, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.1m, TopSpeedKmh = 205, FuelConsumptionCombined = 6.3m },
                new EngineVersion { EngineName = "e-Power HEV 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1497, FuelTypeId = hyb,
                    TorqueNm = 330, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 7.9m, TopSpeedKmh = 170, FuelConsumptionCombined = 5.3m },
            ]);

            int xtrail = GetOrCreateModel(nissanId, "X-Trail", "nissan-x-trail");
            PrepareGenerations(xtrail,
                ("T32 (2014–2022)", "nissan-xtrail-t32", 2014, 2022),
                ("T33 (2022–)", "nissan-xtrail-t33", 2022, null));
            AddOrReplaceEngines(GetOrFixGeneration(xtrail, "T32 (2014–2022)", "nissan-xtrail-t32", 2014, 2022), 130, [
                new EngineVersion { EngineName = "1.3 DIG-T 160 KM", PowerHP = 160, PowerKW = 118, Displacement = 1332, FuelTypeId = mild,
                    TorqueNm = 240, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.2m, TopSpeedKmh = 190, FuelConsumptionCombined = 6.6m },
                new EngineVersion { EngineName = "1.7 dCi 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1749, FuelTypeId = die,
                    TorqueNm = 340, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 194, FuelConsumptionCombined = 5.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(xtrail, "T33 (2022–)", "nissan-xtrail-t33", 2022, null), 160, [
                new EngineVersion { EngineName = "e-Power HEV 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 1497, FuelTypeId = hyb,
                    TorqueNm = 330, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 8.1m, TopSpeedKmh = 170, FuelConsumptionCombined = 6.1m },
                new EngineVersion { EngineName = "e-4ORCE e-Power 213 KM AWD", PowerHP = 213, PowerKW = 157, Displacement = 1497, FuelTypeId = hyb,
                    TorqueNm = 330, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 3, Acceleration0100 = 7.0m, TopSpeedKmh = 170, FuelConsumptionCombined = 6.7m },
            ]);

            int micra = GetOrCreateModel(nissanId, "Micra", "nissan-micra");
            AddOrReplaceEngines(GetOrFixGeneration(micra, "K14 (2016–2024)", "nissan-micra-k14", 2016, 2024), 70, [
                new EngineVersion { EngineName = "1.0 IG-T 92 KM", PowerHP = 92, PowerKW = 68, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 160, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.4m, TopSpeedKmh = 175, FuelConsumptionCombined = 5.2m },
                new EngineVersion { EngineName = "1.0 IG-T 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 160, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.4m, TopSpeedKmh = 183, FuelConsumptionCombined = 5.3m },
            ]);

            int navara = GetOrCreateModel(nissanId, "Navara", "nissan-navara");
            AddOrReplaceEngines(GetOrFixGeneration(navara, "D23 (2015–)", "nissan-navara-d23", 2015, null), 150, [
                new EngineVersion { EngineName = "2.3 dCi 160 KM", PowerHP = 160, PowerKW = 118, Displacement = 2298, FuelTypeId = die,
                    TorqueNm = 403, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 12.0m, TopSpeedKmh = 173, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "2.3 dCi 190 KM Twin-Turbo", PowerHP = 190, PowerKW = 140, Displacement = 2298, FuelTypeId = die,
                    TorqueNm = 450, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 10.7m, TopSpeedKmh = 180, FuelConsumptionCombined = 7.9m },
            ]);

            int ariya = GetOrCreateModel(nissanId, "Ariya", "nissan-ariya");
            AddOrReplaceEngines(GetOrFixGeneration(ariya, "I (2022–)", "nissan-ariya-i", 2022, null), 200, [
                new EngineVersion { EngineName = "63 kWh 218 KM", PowerHP = 218, PowerKW = 160, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 0, Acceleration0100 = 7.5m, TopSpeedKmh = 160, FuelConsumptionCombined = 17.1m },
                new EngineVersion { EngineName = "87 kWh e-4ORCE 306 KM AWD", PowerHP = 306, PowerKW = 225, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 600, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 5.7m, TopSpeedKmh = 200, FuelConsumptionCombined = 19.5m },
            ]);

            int gtr = GetOrCreateModel(nissanId, "GT-R", "nissan-gt-r");
            AddOrReplaceEngines(GetOrFixGeneration(gtr, "R35 (2007–)", "nissan-gtr-r35", 2007, null), 480, [
                new EngineVersion { EngineName = "3.8 V6 Bi-Turbo 570 KM", PowerHP = 570, PowerKW = 419, Displacement = 3799, FuelTypeId = ben,
                    TorqueNm = 637, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 2.9m, TopSpeedKmh = 315, FuelConsumptionCombined = 11.7m },
                new EngineVersion { EngineName = "Nismo 3.8 V6 600 KM", PowerHP = 600, PowerKW = 441, Displacement = 3799, FuelTypeId = ben,
                    TorqueNm = 652, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 2.7m, TopSpeedKmh = 315, FuelConsumptionCombined = 12.0m },
            ]);

            int z370 = GetOrCreateModel(nissanId, "370Z", "nissan-370z");
            AddOrReplaceEngines(GetOrFixGeneration(z370, "Z34 (2009–2020)", "nissan-370z-z34", 2009, 2020), 300, [
                new EngineVersion { EngineName = "3.7 V6 328 KM", PowerHP = 328, PowerKW = 241, Displacement = 3696, FuelTypeId = ben,
                    TorqueNm = 366, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 5.3m, TopSpeedKmh = 250, FuelConsumptionCombined = 10.5m },
            ]);

            // ── DACIA DUSTER II + III ─────────────────────────────────────────────────
            int daciaId = GetOrCreateBrand("Dacia", "dacia", "auta-osobowe");
            int duster = GetOrCreateModel(daciaId, "Duster", "dacia-duster");
            AddEngines(GetOrCreateGeneration(duster, "II (2017–2023)", "dacia-duster-ii", 2017, 2023), [
                new EngineVersion { EngineName = "1.0 TCe 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 160, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 13.5m, TopSpeedKmh = 164, FuelConsumptionCombined = 6.1m },
                new EngineVersion { EngineName = "1.3 TCe 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1332, FuelTypeId = ben,
                    TorqueNm = 240, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.0m, TopSpeedKmh = 186, FuelConsumptionCombined = 6.8m },
                new EngineVersion { EngineName = "1.5 dCi 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 220, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.5m, TopSpeedKmh = 165, FuelConsumptionCombined = 4.3m },
                new EngineVersion { EngineName = "1.5 dCi 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 260, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 183, FuelConsumptionCombined = 4.6m },
            ]);
            AddEngines(GetOrCreateGeneration(duster, "III (2023–)", "dacia-duster-iii", 2023, null), [
                new EngineVersion { EngineName = "1.2 TCe 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.9m, TopSpeedKmh = 185, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "1.2 TCe Hybrid 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1199, FuelTypeId = hyb,
                    TorqueNm = 205, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.7m, TopSpeedKmh = 172, FuelConsumptionCombined = 5.1m },
            ]);

            int dokker = GetOrCreateModel(daciaId, "Dokker", "dacia-dokker");
            AddOrReplaceEngines(GetOrFixGeneration(dokker, "I (2012–)", "dacia-dokker-i", 2012, null), 60, [
                new EngineVersion { EngineName = "1.6 85 KM", PowerHP = 85, PowerKW = 63, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 148, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.5m, TopSpeedKmh = 158, FuelConsumptionCombined = 6.8m },
                new EngineVersion { EngineName = "TCe 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1197, FuelTypeId = ben,
                    TorqueNm = 152, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.5m, TopSpeedKmh = 172, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "1.5 dCi 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 220, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.5m, TopSpeedKmh = 165, FuelConsumptionCombined = 4.5m },
                new EngineVersion { EngineName = "1.5 dCi 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 260, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 175, FuelConsumptionCombined = 4.7m },
            ]);

            int jogger = GetOrCreateModel(daciaId, "Jogger", "dacia-jogger");
            AddOrReplaceEngines(GetOrFixGeneration(jogger, "I (2022–)", "dacia-jogger-i", 2022, null), 100, [
                new EngineVersion { EngineName = "TCe 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.5m, TopSpeedKmh = 168, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "Hybrid 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1598, FuelTypeId = hyb,
                    TorqueNm = 144, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.1m, TopSpeedKmh = 160, FuelConsumptionCombined = 4.6m },
            ]);

            int lodgy = GetOrCreateModel(daciaId, "Lodgy", "dacia-lodgy");
            AddOrReplaceEngines(GetOrFixGeneration(lodgy, "I (2012–2022)", "dacia-lodgy-i", 2012, 2022), 90, [
                new EngineVersion { EngineName = "TCe 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1197, FuelTypeId = ben,
                    TorqueNm = 190, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.4m, TopSpeedKmh = 175, FuelConsumptionCombined = 6.7m },
                new EngineVersion { EngineName = "1.5 dCi 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 220, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.3m, TopSpeedKmh = 165, FuelConsumptionCombined = 4.6m },
                new EngineVersion { EngineName = "1.5 dCi 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 260, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.7m, TopSpeedKmh = 175, FuelConsumptionCombined = 4.8m },
            ]);

            int logan = GetOrCreateModel(daciaId, "Logan", "dacia-logan");
            PrepareGenerations(logan,
                ("I (2004–2012)", "dacia-logan-i", 2004, 2012),
                ("II (2012–2020)", "dacia-logan-ii", 2012, 2020),
                ("III (2020–)", "dacia-logan-iii", 2020, null));
            AddOrReplaceEngines(GetOrFixGeneration(logan, "I (2004–2012)", "dacia-logan-i", 2004, 2012), 60, [
                new EngineVersion { EngineName = "1.4 75 KM", PowerHP = 75, PowerKW = 55, Displacement = 1390, FuelTypeId = ben,
                    TorqueNm = 112, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 15.9m, TopSpeedKmh = 150, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "1.6 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 128, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.0m, TopSpeedKmh = 165, FuelConsumptionCombined = 6.9m },
                new EngineVersion { EngineName = "1.5 dCi 68 KM", PowerHP = 68, PowerKW = 50, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 160, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 16.9m, TopSpeedKmh = 148, FuelConsumptionCombined = 4.5m },
                new EngineVersion { EngineName = "1.5 dCi 85 KM", PowerHP = 85, PowerKW = 63, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 200, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.4m, TopSpeedKmh = 160, FuelConsumptionCombined = 4.6m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(logan, "II (2012–2020)", "dacia-logan-ii", 2012, 2020), 60, [
                new EngineVersion { EngineName = "1.2 75 KM", PowerHP = 75, PowerKW = 55, Displacement = 1149, FuelTypeId = ben,
                    TorqueNm = 107, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.6m, TopSpeedKmh = 158, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "TCe 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 898, FuelTypeId = ben,
                    TorqueNm = 135, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.1m, TopSpeedKmh = 173, FuelConsumptionCombined = 5.3m },
                new EngineVersion { EngineName = "1.5 dCi 75 KM", PowerHP = 75, PowerKW = 55, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 180, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 16.0m, TopSpeedKmh = 158, FuelConsumptionCombined = 4.0m },
                new EngineVersion { EngineName = "1.5 dCi 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 220, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.4m, TopSpeedKmh = 168, FuelConsumptionCombined = 4.1m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(logan, "III (2020–)", "dacia-logan-iii", 2020, null), 80, [
                new EngineVersion { EngineName = "TCe 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 160, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.7m, TopSpeedKmh = 169, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "TCe 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 160, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.3m, TopSpeedKmh = 174, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "Bi-Fuel LPG 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 999, FuelTypeId = lpg,
                    TorqueNm = 170, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.5m, TopSpeedKmh = 172, FuelConsumptionCombined = 6.9m },
            ]);

            int loganMcv = GetOrCreateModel(daciaId, "Logan MCV", "dacia-logan-mcv");
            PrepareGenerations(loganMcv,
                ("I (2007–2012)", "dacia-logan-mcv-i", 2007, 2012),
                ("II (2013–2020)", "dacia-logan-mcv-ii", 2013, 2020));
            AddOrReplaceEngines(GetOrFixGeneration(loganMcv, "I (2007–2012)", "dacia-logan-mcv-i", 2007, 2012), 60, [
                new EngineVersion { EngineName = "1.4 75 KM", PowerHP = 75, PowerKW = 55, Displacement = 1390, FuelTypeId = ben,
                    TorqueNm = 112, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 16.4m, TopSpeedKmh = 148, FuelConsumptionCombined = 6.7m },
                new EngineVersion { EngineName = "1.6 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 128, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.5m, TopSpeedKmh = 163, FuelConsumptionCombined = 7.1m },
                new EngineVersion { EngineName = "1.5 dCi 85 KM", PowerHP = 85, PowerKW = 63, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 200, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.9m, TopSpeedKmh = 158, FuelConsumptionCombined = 4.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(loganMcv, "II (2013–2020)", "dacia-logan-mcv-ii", 2013, 2020), 60, [
                new EngineVersion { EngineName = "TCe 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 898, FuelTypeId = ben,
                    TorqueNm = 135, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.5m, TopSpeedKmh = 170, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "1.5 dCi 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 220, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.9m, TopSpeedKmh = 165, FuelConsumptionCombined = 4.3m },
            ]);

            int sandero = GetOrCreateModel(daciaId, "Sandero", "dacia-sandero");
            PrepareGenerations(sandero,
                ("I (2008–2012)", "dacia-sandero-i", 2008, 2012),
                ("II (2012–2020)", "dacia-sandero-ii", 2012, 2020),
                ("III (2020–)", "dacia-sandero-iii", 2020, null));
            AddOrReplaceEngines(GetOrFixGeneration(sandero, "I (2008–2012)", "dacia-sandero-i", 2008, 2012), 60, [
                new EngineVersion { EngineName = "1.4 75 KM", PowerHP = 75, PowerKW = 55, Displacement = 1390, FuelTypeId = ben,
                    TorqueNm = 112, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 15.9m, TopSpeedKmh = 150, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "1.6 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 128, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.0m, TopSpeedKmh = 165, FuelConsumptionCombined = 6.9m },
                new EngineVersion { EngineName = "1.5 dCi 70 KM", PowerHP = 70, PowerKW = 51, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 160, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 16.4m, TopSpeedKmh = 149, FuelConsumptionCombined = 4.5m },
                new EngineVersion { EngineName = "1.5 dCi 85 KM", PowerHP = 85, PowerKW = 63, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 200, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.4m, TopSpeedKmh = 160, FuelConsumptionCombined = 4.6m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(sandero, "II (2012–2020)", "dacia-sandero-ii", 2012, 2020), 60, [
                new EngineVersion { EngineName = "0.9 TCe 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 898, FuelTypeId = ben,
                    TorqueNm = 135, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.1m, TopSpeedKmh = 173, FuelConsumptionCombined = 5.2m },
                new EngineVersion { EngineName = "1.5 dCi 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 220, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.4m, TopSpeedKmh = 168, FuelConsumptionCombined = 4.1m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(sandero, "III (2020–)", "dacia-sandero-iii", 2020, null), 80, [
                new EngineVersion { EngineName = "TCe 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 160, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.7m, TopSpeedKmh = 169, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "TCe 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 160, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.3m, TopSpeedKmh = 174, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "Bi-Fuel LPG 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 999, FuelTypeId = lpg,
                    TorqueNm = 170, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.5m, TopSpeedKmh = 172, FuelConsumptionCombined = 6.9m },
            ]);

            int sanderoStepway = GetOrCreateModel(daciaId, "Sandero Stepway", "dacia-sandero-stepway");
            PrepareGenerations(sanderoStepway,
                ("I (2009–2012)", "dacia-sandero-stepway-i", 2009, 2012),
                ("II (2013–2020)", "dacia-sandero-stepway-ii", 2013, 2020));
            AddOrReplaceEngines(GetOrFixGeneration(sanderoStepway, "I (2009–2012)", "dacia-sandero-stepway-i", 2009, 2012), 60, [
                new EngineVersion { EngineName = "1.6 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 128, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.5m, TopSpeedKmh = 163, FuelConsumptionCombined = 7.1m },
                new EngineVersion { EngineName = "1.5 dCi 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 200, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.8m, TopSpeedKmh = 158, FuelConsumptionCombined = 4.7m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(sanderoStepway, "II (2013–2020)", "dacia-sandero-stepway-ii", 2013, 2020), 60, [
                new EngineVersion { EngineName = "TCe 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 898, FuelTypeId = ben,
                    TorqueNm = 135, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.6m, TopSpeedKmh = 171, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "1.5 dCi 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 220, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.9m, TopSpeedKmh = 165, FuelConsumptionCombined = 4.3m },
            ]);

            int spring = GetOrCreateModel(daciaId, "Spring", "dacia-spring");
            AddOrReplaceEngines(GetOrFixGeneration(spring, "I (2021–)", "dacia-spring-i", 2021, null), 40, [
                new EngineVersion { EngineName = "45 KM", PowerHP = 45, PowerKW = 33, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 125, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = null, Acceleration0100 = 19.1m, TopSpeedKmh = 125, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "65 KM", PowerHP = 65, PowerKW = 48, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 125, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = null, Acceleration0100 = 13.7m, TopSpeedKmh = 125, FuelConsumptionCombined = null },
            ]);

            // ── MAZDA CX-5 KF + MAZDA3 BP ────────────────────────────────────────────
            int mazdaId = GetOrCreateBrand("Mazda", "mazda", "auta-osobowe");
            int cx5 = GetOrCreateModel(mazdaId, "CX-5", "mazda-cx5");
            AddEngines(GetOrCreateGeneration(cx5, "KF (2017–)", "mazda-cx5-kf", 2017, null), [
                new EngineVersion { EngineName = "2.0 Skyactiv-G 165 KM", PowerHP = 165, PowerKW = 121, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 213, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.0m, TopSpeedKmh = 195, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "2.5 Skyactiv-G 194 KM", PowerHP = 194, PowerKW = 143, Displacement = 2488, FuelTypeId = ben,
                    TorqueNm = 252, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.3m, TopSpeedKmh = 209, FuelConsumptionCombined = 8.1m },
                new EngineVersion { EngineName = "2.5 Skyactiv-G Turbo 231 KM", PowerHP = 231, PowerKW = 170, Displacement = 2488, FuelTypeId = ben,
                    TorqueNm = 420, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.0m, TopSpeedKmh = 220, FuelConsumptionCombined = 9.0m },
                new EngineVersion { EngineName = "2.2 Skyactiv-D 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 2191, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.4m, TopSpeedKmh = 194, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "2.2 Skyactiv-D 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 2191, FuelTypeId = die,
                    TorqueNm = 445, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.2m, TopSpeedKmh = 203, FuelConsumptionCombined = 5.8m },
            ]);
            int mazda3 = GetOrCreateModel(mazdaId, "Mazda3", "mazda-3");
            AddEngines(GetOrCreateGeneration(mazda3, "BP (2018–)", "mazda3-bp", 2018, null), [
                new EngineVersion { EngineName = "2.0 Skyactiv-G 122 KM", PowerHP = 122, PowerKW = 90, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 213, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.0m, TopSpeedKmh = 195, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "2.0 e-Skyactiv-X 186 KM", PowerHP = 186, PowerKW = 137, Displacement = 1998, FuelTypeId = mild,
                    TorqueNm = 240, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.3m, TopSpeedKmh = 215, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "1.8 Skyactiv-D 116 KM", PowerHP = 116, PowerKW = 85, Displacement = 1756, FuelTypeId = die,
                    TorqueNm = 270, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.0m, TopSpeedKmh = 197, FuelConsumptionCombined = 4.9m },
            ]);

            // ── MASSEY FERGUSON ──────────────────────────────────────────────────────
            int mfId = GetOrCreateBrand("Massey Ferguson", "massey-ferguson", "maszyny-rolnicze");
            int mf5700s = GetOrCreateModel(mfId, "MF 5700 S", "mf-5700-s");
            AddEngines(GetOrCreateGeneration(mf5700s, "S (2014–)", "mf-5700s-2014", 2014, null), [
                new EngineVersion { EngineName = "5710 S 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 4400, FuelTypeId = die,
                    TorqueNm = 420, EuroNorm = "Stage V", GearboxType = "powershift", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = 40, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "5713 S 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 4485, FuelTypeId = die,
                    TorqueNm = 510, EuroNorm = "Stage V", GearboxType = "powershift", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = 40, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "5715 S 155 KM", PowerHP = 155, PowerKW = 114, Displacement = 4485, FuelTypeId = die,
                    TorqueNm = 600, EuroNorm = "Stage V", GearboxType = "powershift", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = 40, FuelConsumptionCombined = null },
            ]);
            int mf7700s = GetOrCreateModel(mfId, "MF 7700 S", "mf-7700-s");
            AddEngines(GetOrCreateGeneration(mf7700s, "S (2012–)", "mf-7700s-2012", 2012, null), [
                new EngineVersion { EngineName = "7715 S 155 KM", PowerHP = 155, PowerKW = 114, Displacement = 6600, FuelTypeId = die,
                    TorqueNm = 640, EuroNorm = "Stage V", GearboxType = "powershift", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 50, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "7720 S 205 KM", PowerHP = 205, PowerKW = 151, Displacement = 6600, FuelTypeId = die,
                    TorqueNm = 850, EuroNorm = "Stage V", GearboxType = "powershift", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 50, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "7726 S 260 KM", PowerHP = 260, PowerKW = 191, Displacement = 8400, FuelTypeId = die,
                    TorqueNm = 1100, EuroNorm = "Stage V", GearboxType = "powershift", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 50, FuelConsumptionCombined = null },
            ]);
            int mf8700s = GetOrCreateModel(mfId, "MF 8700 S", "mf-8700-s");
            AddEngines(GetOrCreateGeneration(mf8700s, "S (2017–)", "mf-8700s-2017", 2017, null), [
                new EngineVersion { EngineName = "8730 S 305 KM", PowerHP = 305, PowerKW = 224, Displacement = 8400, FuelTypeId = die,
                    TorqueNm = 1280, EuroNorm = "Stage V", GearboxType = "powershift", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 50, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "8737 S 370 KM", PowerHP = 370, PowerKW = 272, Displacement = 8400, FuelTypeId = die,
                    TorqueNm = 1550, EuroNorm = "Stage V", GearboxType = "powershift", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 50, FuelConsumptionCombined = null },
            ]);

            // ── CASE IH ──────────────────────────────────────────────────────────────
            int caseIhId = GetOrCreateBrand("Case IH", "case-ih", "maszyny-rolnicze");
            int casePuma = GetOrCreateModel(caseIhId, "Puma", "case-ih-puma");
            AddEngines(GetOrCreateGeneration(casePuma, "CVX (2014–)", "case-ih-puma-cvx", 2014, null), [
                new EngineVersion { EngineName = "Puma 150 CVX 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 6728, FuelTypeId = die,
                    TorqueNm = 620, EuroNorm = "Stage V", GearboxType = "cvt", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 50, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Puma 185 CVX 185 KM", PowerHP = 185, PowerKW = 136, Displacement = 6728, FuelTypeId = die,
                    TorqueNm = 760, EuroNorm = "Stage V", GearboxType = "cvt", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 50, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Puma 220 CVX 220 KM", PowerHP = 220, PowerKW = 162, Displacement = 6728, FuelTypeId = die,
                    TorqueNm = 900, EuroNorm = "Stage V", GearboxType = "cvt", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 50, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Puma 240 CVX 240 KM", PowerHP = 240, PowerKW = 177, Displacement = 6728, FuelTypeId = die,
                    TorqueNm = 1000, EuroNorm = "Stage V", GearboxType = "cvt", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 50, FuelConsumptionCombined = null },
            ]);
            int caseOptum = GetOrCreateModel(caseIhId, "Optum", "case-ih-optum");
            AddEngines(GetOrCreateGeneration(caseOptum, "AFS Connect (2016–)", "case-ih-optum-afs", 2016, null), [
                new EngineVersion { EngineName = "Optum 250 CVX 250 KM", PowerHP = 250, PowerKW = 184, Displacement = 8700, FuelTypeId = die,
                    TorqueNm = 1050, EuroNorm = "Stage V", GearboxType = "cvt", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 50, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Optum 270 CVX 270 KM", PowerHP = 270, PowerKW = 199, Displacement = 8700, FuelTypeId = die,
                    TorqueNm = 1150, EuroNorm = "Stage V", GearboxType = "cvt", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 50, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Optum 300 CVX 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 8700, FuelTypeId = die,
                    TorqueNm = 1280, EuroNorm = "Stage V", GearboxType = "cvt", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 50, FuelConsumptionCombined = null },
            ]);
            int caseMaxxum = GetOrCreateModel(caseIhId, "Maxxum", "case-ih-maxxum");
            AddEngines(GetOrCreateGeneration(caseMaxxum, "AFS Connect (2017–)", "case-ih-maxxum-afs", 2017, null), [
                new EngineVersion { EngineName = "Maxxum 115 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 4485, FuelTypeId = die,
                    TorqueNm = 470, EuroNorm = "Stage V", GearboxType = "powershift", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = 40, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Maxxum 135 135 KM", PowerHP = 135, PowerKW = 99, Displacement = 4485, FuelTypeId = die,
                    TorqueNm = 560, EuroNorm = "Stage V", GearboxType = "powershift", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = 40, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Maxxum 150 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 6728, FuelTypeId = die,
                    TorqueNm = 620, EuroNorm = "Stage V", GearboxType = "powershift", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 40, FuelConsumptionCombined = null },
            ]);

            // ── CLAAS ────────────────────────────────────────────────────────────────
            int claasId = GetOrCreateBrand("Claas", "claas", "maszyny-rolnicze");
            int axion900 = GetOrCreateModel(claasId, "Axion 900", "claas-axion-900");
            AddEngines(GetOrCreateGeneration(axion900, "CIS+ (2015–)", "claas-axion900-cis", 2015, null), [
                new EngineVersion { EngineName = "Axion 920 205 KM", PowerHP = 205, PowerKW = 151, Displacement = 6800, FuelTypeId = die,
                    TorqueNm = 880, EuroNorm = "Stage V", GearboxType = "cvt", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 50, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Axion 940 245 KM", PowerHP = 245, PowerKW = 180, Displacement = 6800, FuelTypeId = die,
                    TorqueNm = 1050, EuroNorm = "Stage V", GearboxType = "cvt", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 50, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Axion 960 290 KM", PowerHP = 290, PowerKW = 213, Displacement = 6800, FuelTypeId = die,
                    TorqueNm = 1250, EuroNorm = "Stage V", GearboxType = "cvt", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 50, FuelConsumptionCombined = null },
            ]);
            int axion800 = GetOrCreateModel(claasId, "Axion 800", "claas-axion-800");
            AddEngines(GetOrCreateGeneration(axion800, "CIS+ (2012–)", "claas-axion800-cis", 2012, null), [
                new EngineVersion { EngineName = "Axion 810 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 6800, FuelTypeId = die,
                    TorqueNm = 780, EuroNorm = "Stage V", GearboxType = "cvt", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 40, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Axion 840 205 KM", PowerHP = 205, PowerKW = 151, Displacement = 6800, FuelTypeId = die,
                    TorqueNm = 880, EuroNorm = "Stage V", GearboxType = "cvt", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 40, FuelConsumptionCombined = null },
            ]);
            int arion600 = GetOrCreateModel(claasId, "Arion 600", "claas-arion-600");
            AddEngines(GetOrCreateGeneration(arion600, "CIS (2012–)", "claas-arion600-cis", 2012, null), [
                new EngineVersion { EngineName = "Arion 610 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 4485, FuelTypeId = die,
                    TorqueNm = 480, EuroNorm = "Stage V", GearboxType = "powershift", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = 40, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Arion 640 155 KM", PowerHP = 155, PowerKW = 114, Displacement = 4485, FuelTypeId = die,
                    TorqueNm = 640, EuroNorm = "Stage V", GearboxType = "powershift", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = 40, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Arion 660 185 KM", PowerHP = 185, PowerKW = 136, Displacement = 6728, FuelTypeId = die,
                    TorqueNm = 760, EuroNorm = "Stage V", GearboxType = "powershift", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 40, FuelConsumptionCombined = null },
            ]);

            // ── ZETOR ────────────────────────────────────────────────────────────────
            int zetorId = GetOrCreateBrand("Zetor", "zetor", "maszyny-rolnicze");
            int zetorMajor = GetOrCreateModel(zetorId, "Major", "zetor-major");
            AddEngines(GetOrCreateGeneration(zetorMajor, "CL (2012–)", "zetor-major-cl", 2012, null), [
                new EngineVersion { EngineName = "Major CL 80 80 KM", PowerHP = 80, PowerKW = 59, Displacement = 3792, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Stage V", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = 40, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Major CL 100 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 3792, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Stage V", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = 40, FuelConsumptionCombined = null },
            ]);
            int zetorForterra = GetOrCreateModel(zetorId, "Forterra", "zetor-forterra");
            AddEngines(GetOrCreateGeneration(zetorForterra, "HD (2012–)", "zetor-forterra-hd", 2012, null), [
                new EngineVersion { EngineName = "Forterra 100 HD 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 4156, FuelTypeId = die,
                    TorqueNm = 410, EuroNorm = "Stage V", GearboxType = "powershift", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = 40, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Forterra 130 HD 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 4156, FuelTypeId = die,
                    TorqueNm = 530, EuroNorm = "Stage V", GearboxType = "powershift", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = 40, FuelConsumptionCombined = null },
            ]);

            // ── KUBOTA ───────────────────────────────────────────────────────────────
            int kubotaId = GetOrCreateBrand("Kubota", "kubota", "maszyny-rolnicze");
            int kubotaM7 = GetOrCreateModel(kubotaId, "M7", "kubota-m7");
            AddEngines(GetOrCreateGeneration(kubotaM7, "M7-151 (2014–)", "kubota-m7-2014", 2014, null), [
                new EngineVersion { EngineName = "V6108 152 KM", PowerHP = 152, PowerKW = 112, Displacement = 6108, FuelTypeId = die,
                    TorqueNm = 650, EuroNorm = "Stage V", GearboxType = "cvt", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 40, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "V6108 172 KM", PowerHP = 172, PowerKW = 126, Displacement = 6108, FuelTypeId = die,
                    TorqueNm = 730, EuroNorm = "Stage V", GearboxType = "cvt", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 40, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "V6108 192 KM", PowerHP = 192, PowerKW = 141, Displacement = 6108, FuelTypeId = die,
                    TorqueNm = 820, EuroNorm = "Stage V", GearboxType = "cvt", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 40, FuelConsumptionCombined = null },
            ]);

            // ── VOLVO TRUCKS FH + FM ──────────────────────────────────────────────────
            int volvoTrucksId = GetOrCreateBrand("Volvo", "volvo", "ciezarowki");
            int volvoFH = GetOrCreateModel(volvoTrucksId, "FH", "volvo-fh");
            AddEngines(GetOrCreateGeneration(volvoFH, "IV (2012–2020)", "volvo-fh-iv", 2012, 2020), [
                new EngineVersion { EngineName = "D13 420 KM", PowerHP = 420, PowerKW = 309, Displacement = 12777, FuelTypeId = die,
                    TorqueNm = 2100, EuroNorm = "Euro 6", GearboxType = "powershift", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "D13 460 KM", PowerHP = 460, PowerKW = 338, Displacement = 12777, FuelTypeId = die,
                    TorqueNm = 2300, EuroNorm = "Euro 6", GearboxType = "powershift", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "D13 500 KM", PowerHP = 500, PowerKW = 368, Displacement = 12777, FuelTypeId = die,
                    TorqueNm = 2500, EuroNorm = "Euro 6", GearboxType = "powershift", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "D16 550 KM", PowerHP = 550, PowerKW = 404, Displacement = 16120, FuelTypeId = die,
                    TorqueNm = 2750, EuroNorm = "Euro 6", GearboxType = "powershift", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
            ]);
            AddEngines(GetOrCreateGeneration(volvoFH, "V (2020–)", "volvo-fh-v", 2020, null), [
                new EngineVersion { EngineName = "D13 420 KM", PowerHP = 420, PowerKW = 309, Displacement = 12777, FuelTypeId = die,
                    TorqueNm = 2100, EuroNorm = "Euro 6d", GearboxType = "powershift", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "D13 460 KM", PowerHP = 460, PowerKW = 338, Displacement = 12777, FuelTypeId = die,
                    TorqueNm = 2300, EuroNorm = "Euro 6d", GearboxType = "powershift", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "D13 500 KM", PowerHP = 500, PowerKW = 368, Displacement = 12777, FuelTypeId = die,
                    TorqueNm = 2500, EuroNorm = "Euro 6d", GearboxType = "powershift", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
            ]);
            int volvoFM = GetOrCreateModel(volvoTrucksId, "FM", "volvo-fm");
            AddEngines(GetOrCreateGeneration(volvoFM, "IV (2012–)", "volvo-fm-iv", 2012, null), [
                new EngineVersion { EngineName = "D11 330 KM", PowerHP = 330, PowerKW = 243, Displacement = 10837, FuelTypeId = die,
                    TorqueNm = 1600, EuroNorm = "Euro 6", GearboxType = "powershift", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "D11 370 KM", PowerHP = 370, PowerKW = 272, Displacement = 10837, FuelTypeId = die,
                    TorqueNm = 1800, EuroNorm = "Euro 6", GearboxType = "powershift", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "D13 430 KM", PowerHP = 430, PowerKW = 316, Displacement = 12777, FuelTypeId = die,
                    TorqueNm = 2100, EuroNorm = "Euro 6", GearboxType = "powershift", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "D13 460 KM", PowerHP = 460, PowerKW = 338, Displacement = 12777, FuelTypeId = die,
                    TorqueNm = 2300, EuroNorm = "Euro 6", GearboxType = "powershift", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
            ]);

            // ── PEUGEOT 208 + 308 + 3008 ─────────────────────────────────────────────
            int peugeotId = GetOrCreateBrand("Peugeot", "peugeot", "auta-osobowe");
            int p208 = GetOrCreateModel(peugeotId, "208", "peugeot-208");
            AddEngines(GetOrCreateGeneration(p208, "II (2019–)", "peugeot-208-ii", 2019, null), [
                new EngineVersion { EngineName = "1.2 PureTech 75 KM", PowerHP = 75, PowerKW = 55, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 118, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 14.5m, TopSpeedKmh = 167, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "1.2 PureTech 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 205, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.4m, TopSpeedKmh = 188, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "1.2 PureTech 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 8.8m, TopSpeedKmh = 208, FuelConsumptionCombined = 5.7m },
                new EngineVersion { EngineName = "1.5 BlueHDi 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.7m, TopSpeedKmh = 188, FuelConsumptionCombined = 3.8m },
            ]);
            int p308 = GetOrCreateModel(peugeotId, "308", "peugeot-308");
            AddEngines(GetOrCreateGeneration(p308, "II (2013–2021)", "peugeot-308-ii", 2013, 2021), [
                new EngineVersion { EngineName = "1.2 PureTech 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 205, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.0m, TopSpeedKmh = 193, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "1.2 PureTech 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.0m, TopSpeedKmh = 208, FuelConsumptionCombined = 5.7m },
                new EngineVersion { EngineName = "GTi 1.6 THP 270 KM", PowerHP = 270, PowerKW = 199, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 330, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.0m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.7m },
                new EngineVersion { EngineName = "1.5 BlueHDi 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 193, FuelConsumptionCombined = 4.0m },
                new EngineVersion { EngineName = "2.0 BlueHDi 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 370, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.8m, TopSpeedKmh = 218, FuelConsumptionCombined = 4.4m },
            ]);
            AddEngines(GetOrCreateGeneration(p308, "III (2021–)", "peugeot-308-iii", 2021, null), [
                new EngineVersion { EngineName = "1.2 PureTech 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.0m, TopSpeedKmh = 207, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "1.6 Hybrid 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 1598, FuelTypeId = hyb,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 220, FuelConsumptionCombined = 4.8m },
                new EngineVersion { EngineName = "1.5 BlueHDi 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 210, FuelConsumptionCombined = 4.1m },
                new EngineVersion { EngineName = "PHEV 225 KM", PowerHP = 225, PowerKW = 165, Displacement = 1598, FuelTypeId = phev,
                    TorqueNm = 360, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 225, FuelConsumptionCombined = 1.3m },
            ]);
            int p3008 = GetOrCreateModel(peugeotId, "3008", "peugeot-3008");
            AddEngines(GetOrCreateGeneration(p3008, "II (2016–2023)", "peugeot-3008-ii", 2016, 2023), [
                new EngineVersion { EngineName = "1.2 PureTech 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.5m, TopSpeedKmh = 202, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "1.6 THP 165 KM", PowerHP = 165, PowerKW = 121, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 240, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 213, FuelConsumptionCombined = 7.1m },
                new EngineVersion { EngineName = "1.5 BlueHDi 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.6m, TopSpeedKmh = 202, FuelConsumptionCombined = 4.3m },
                new EngineVersion { EngineName = "2.0 BlueHDi 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 215, FuelConsumptionCombined = 5.0m },
                new EngineVersion { EngineName = "PHEV 225 KM", PowerHP = 225, PowerKW = 165, Displacement = 1598, FuelTypeId = phev,
                    TorqueNm = 360, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.8m, TopSpeedKmh = 225, FuelConsumptionCombined = 1.5m },
            ]);

            // ── HONDA CIVIC X + XI ────────────────────────────────────────────────────
            int hondaId = GetOrCreateBrand("Honda", "honda", "auta-osobowe");
            int civic = GetOrCreateModel(hondaId, "Civic", "honda-civic");
            AddEngines(GetOrCreateGeneration(civic, "X (2017–2021)", "honda-civic-x", 2017, 2021), [
                new EngineVersion { EngineName = "1.0 VTEC Turbo 126 KM", PowerHP = 126, PowerKW = 93, Displacement = 988, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.6m, TopSpeedKmh = 200, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "1.5 VTEC Turbo 182 KM", PowerHP = 182, PowerKW = 134, Displacement = 1498, FuelTypeId = ben,
                    TorqueNm = 240, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.9m, TopSpeedKmh = 220, FuelConsumptionCombined = 6.4m },
                new EngineVersion { EngineName = "Type R 2.0 320 KM", PowerHP = 320, PowerKW = 235, Displacement = 1996, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 5.7m, TopSpeedKmh = 272, FuelConsumptionCombined = 9.6m },
                new EngineVersion { EngineName = "1.6 i-DTEC 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1597, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 202, FuelConsumptionCombined = 4.1m },
            ]);
            AddEngines(GetOrCreateGeneration(civic, "XI (2021–)", "honda-civic-xi", 2021, null), [
                new EngineVersion { EngineName = "1.5 VTEC Turbo 182 KM", PowerHP = 182, PowerKW = 134, Displacement = 1498, FuelTypeId = ben,
                    TorqueNm = 240, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.7m, TopSpeedKmh = 218, FuelConsumptionCombined = 6.4m },
                new EngineVersion { EngineName = "Type R 2.0 329 KM", PowerHP = 329, PowerKW = 242, Displacement = 1996, FuelTypeId = ben,
                    TorqueNm = 420, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 5.4m, TopSpeedKmh = 275, FuelConsumptionCombined = 9.5m },
                new EngineVersion { EngineName = "e:HEV 2.0 143 KM", PowerHP = 143, PowerKW = 105, Displacement = 1993, FuelTypeId = hyb,
                    TorqueNm = 315, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.6m, TopSpeedKmh = 180, FuelConsumptionCombined = 4.7m },
            ]);

            // ── SKODA FABIA III + IV ──────────────────────────────────────────────────
            int skodaId2 = GetOrCreateBrand("Skoda", "skoda", "auta-osobowe");
            int fabia = GetOrCreateModel(skodaId2, "Fabia", "skoda-fabia");
            AddEngines(GetOrCreateGeneration(fabia, "III (2014–2021)", "skoda-fabia-iii", 2014, 2021), [
                new EngineVersion { EngineName = "1.0 MPI 60 KM", PowerHP = 60, PowerKW = 44, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 95, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 16.5m, TopSpeedKmh = 162, FuelConsumptionCombined = 5.3m },
                new EngineVersion { EngineName = "1.0 TSI 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 175, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.0m, TopSpeedKmh = 187, FuelConsumptionCombined = 5.1m },
                new EngineVersion { EngineName = "1.4 TSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1395, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.8m, TopSpeedKmh = 220, FuelConsumptionCombined = 6.1m },
                new EngineVersion { EngineName = "1.4 TDI 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1422, FuelTypeId = die,
                    TorqueNm = 230, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 12.0m, TopSpeedKmh = 182, FuelConsumptionCombined = 3.5m },
            ]);
            AddEngines(GetOrCreateGeneration(fabia, "IV (2021–)", "skoda-fabia-iv", 2021, null), [
                new EngineVersion { EngineName = "1.0 MPI 65 KM", PowerHP = 65, PowerKW = 48, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 95, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 15.5m, TopSpeedKmh = 166, FuelConsumptionCombined = 5.2m },
                new EngineVersion { EngineName = "1.0 TSI 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 175, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.0m, TopSpeedKmh = 187, FuelConsumptionCombined = 5.0m },
                new EngineVersion { EngineName = "1.5 TSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1498, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 221, FuelConsumptionCombined = 5.5m },
            ]);

            // ── SUZUKI SWIFT V + VITARA IV ────────────────────────────────────────────
            int suzukiId2 = GetOrCreateBrand("Suzuki", "suzuki", "auta-osobowe");
            int swift = GetOrCreateModel(suzukiId2, "Swift", "suzuki-swift");
            AddEngines(GetOrCreateGeneration(swift, "V (2017–)", "suzuki-swift-v", 2017, null), [
                new EngineVersion { EngineName = "1.0 Boosterjet 111 KM", PowerHP = 111, PowerKW = 82, Displacement = 998, FuelTypeId = mild,
                    TorqueNm = 170, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.0m, TopSpeedKmh = 195, FuelConsumptionCombined = 5.2m },
                new EngineVersion { EngineName = "1.2 DualJet 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1197, FuelTypeId = hyb,
                    TorqueNm = 120, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.8m, TopSpeedKmh = 185, FuelConsumptionCombined = 4.6m },
                new EngineVersion { EngineName = "Sport 1.4 Boosterjet 129 KM", PowerHP = 129, PowerKW = 95, Displacement = 1373, FuelTypeId = mild,
                    TorqueNm = 235, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.1m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.8m },
            ]);
            int vitara = GetOrCreateModel(suzukiId2, "Vitara", "suzuki-vitara");
            AddEngines(GetOrCreateGeneration(vitara, "IV (2014–)", "suzuki-vitara-iv", 2014, null), [
                new EngineVersion { EngineName = "1.4 Boosterjet 129 KM", PowerHP = 129, PowerKW = 95, Displacement = 1373, FuelTypeId = mild,
                    TorqueNm = 235, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.5m, TopSpeedKmh = 190, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "1.5 Smart Hybrid 102 KM", PowerHP = 102, PowerKW = 75, Displacement = 1462, FuelTypeId = hyb,
                    TorqueNm = 138, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 175, FuelConsumptionCombined = 5.2m },
            ]);

            // ── TOYOTA YARIS XP150 + XP210, RAV4 V, C-HR X10 ────────────────────────
            int toyotaId2 = GetOrCreateBrand("Toyota", "toyota", "auta-osobowe");
            int yaris = GetOrCreateModel(toyotaId2, "Yaris", "toyota-yaris");
            AddEngines(GetOrCreateGeneration(yaris, "XP150 (2011–2019)", "toyota-yaris-xp150", 2011, 2019), [
                new EngineVersion { EngineName = "1.0 VVT-i 69 KM", PowerHP = 69, PowerKW = 51, Displacement = 998, FuelTypeId = ben,
                    TorqueNm = 93, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 14.8m, TopSpeedKmh = 163, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "1.33 VVT-i 99 KM", PowerHP = 99, PowerKW = 73, Displacement = 1329, FuelTypeId = ben,
                    TorqueNm = 131, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.4m, TopSpeedKmh = 180, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "1.5 HEV 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1497, FuelTypeId = hyb,
                    TorqueNm = 111, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.8m, TopSpeedKmh = 165, FuelConsumptionCombined = 3.9m },
                new EngineVersion { EngineName = "1.4 D-4D 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1364, FuelTypeId = die,
                    TorqueNm = 205, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.2m, TopSpeedKmh = 175, FuelConsumptionCombined = 4.0m },
            ]);
            AddEngines(GetOrCreateGeneration(yaris, "XP210 (2019–)", "toyota-yaris-xp210", 2019, null), [
                new EngineVersion { EngineName = "1.5 HEV 116 KM", PowerHP = 116, PowerKW = 85, Displacement = 1490, FuelTypeId = hyb,
                    TorqueNm = 185, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.7m, TopSpeedKmh = 175, FuelConsumptionCombined = 3.8m },
                new EngineVersion { EngineName = "GR Yaris 1.6 261 KM", PowerHP = 261, PowerKW = 192, Displacement = 1618, FuelTypeId = ben,
                    TorqueNm = 360, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 3, Acceleration0100 = 5.5m, TopSpeedKmh = 230, FuelConsumptionCombined = 8.5m },
            ]);
            int rav4 = GetOrCreateModel(toyotaId2, "RAV4", "toyota-rav4");
            AddEngines(GetOrCreateGeneration(rav4, "V (2018–)", "toyota-rav4-v", 2018, null), [
                new EngineVersion { EngineName = "2.0 VVT-i 175 KM", PowerHP = 175, PowerKW = 129, Displacement = 1987, FuelTypeId = ben,
                    TorqueNm = 227, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.4m, TopSpeedKmh = 200, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "2.5 HEV 218 KM", PowerHP = 218, PowerKW = 160, Displacement = 2487, FuelTypeId = hyb,
                    TorqueNm = 221, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.1m, TopSpeedKmh = 180, FuelConsumptionCombined = 5.0m },
                new EngineVersion { EngineName = "2.5 PHEV 306 KM", PowerHP = 306, PowerKW = 225, Displacement = 2487, FuelTypeId = phev,
                    TorqueNm = 270, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.0m, TopSpeedKmh = 180, FuelConsumptionCombined = 1.0m },
            ]);
            int chr = GetOrCreateModel(toyotaId2, "C-HR", "toyota-chr");
            AddEngines(GetOrCreateGeneration(chr, "X10 (2016–2023)", "toyota-chr-x10", 2016, 2023), [
                new EngineVersion { EngineName = "1.2 T 116 KM", PowerHP = 116, PowerKW = 85, Displacement = 1197, FuelTypeId = ben,
                    TorqueNm = 185, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 185, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "1.8 HEV 122 KM", PowerHP = 122, PowerKW = 90, Displacement = 1798, FuelTypeId = hyb,
                    TorqueNm = 142, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 165, FuelConsumptionCombined = 4.5m },
                new EngineVersion { EngineName = "2.0 HEV 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 1987, FuelTypeId = hyb,
                    TorqueNm = 190, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.2m, TopSpeedKmh = 180, FuelConsumptionCombined = 5.0m },
            ]);

            // ── FORD FIESTA Mk7/8 + KUGA Mk2/Mk3 ────────────────────────────────────
            int fordId2 = GetOrCreateBrand("Ford", "ford", "auta-osobowe");
            int fiesta = GetOrCreateModel(fordId2, "Fiesta", "ford-fiesta");
            AddEngines(GetOrCreateGeneration(fiesta, "Mk7 (2008–2017)", "ford-fiesta-mk7", 2008, 2017), [
                new EngineVersion { EngineName = "1.0 EcoBoost 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 170, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.4m, TopSpeedKmh = 192, FuelConsumptionCombined = 5.3m },
                new EngineVersion { EngineName = "1.6 ST 182 KM", PowerHP = 182, PowerKW = 134, Displacement = 1596, FuelTypeId = ben,
                    TorqueNm = 270, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.9m, TopSpeedKmh = 225, FuelConsumptionCombined = 7.3m },
                new EngineVersion { EngineName = "1.4 TDCi 70 KM", PowerHP = 70, PowerKW = 51, Displacement = 1399, FuelTypeId = die,
                    TorqueNm = 160, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.5m, TopSpeedKmh = 160, FuelConsumptionCombined = 3.7m },
            ]);
            AddEngines(GetOrCreateGeneration(fiesta, "Mk8 (2017–)", "ford-fiesta-mk8", 2017, null), [
                new EngineVersion { EngineName = "1.0 EcoBoost 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 170, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.0m, TopSpeedKmh = 183, FuelConsumptionCombined = 5.2m },
                new EngineVersion { EngineName = "1.0 EcoBoost 125 KM", PowerHP = 125, PowerKW = 92, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 210, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.4m, TopSpeedKmh = 200, FuelConsumptionCombined = 5.3m },
                new EngineVersion { EngineName = "1.5 ST 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 1499, FuelTypeId = ben,
                    TorqueNm = 290, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 6.5m, TopSpeedKmh = 232, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "1.5 TDCi 85 KM", PowerHP = 85, PowerKW = 63, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 215, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.5m, TopSpeedKmh = 177, FuelConsumptionCombined = 3.9m },
            ]);
            int kuga = GetOrCreateModel(fordId2, "Kuga", "ford-kuga");
            PrepareGenerations(kuga,
                ("Mk1 (2008–2012)", "ford-kuga-mk1", 2008, 2012),
                ("Mk2 (2012–2019)", "ford-kuga-mk2", 2012, 2019),
                ("Mk3 (2019–)", "ford-kuga-mk3", 2019, null));
            AddOrReplaceEngines(GetOrFixGeneration(kuga, "Mk1 (2008–2012)", "ford-kuga-mk1", 2008, 2012), 130, [
                new EngineVersion { EngineName = "2.0 TDCi 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 180, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "2.0 TDCi 163 KM", PowerHP = 163, PowerKW = 120, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 340, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 190, FuelConsumptionCombined = 6.7m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(kuga, "Mk2 (2012–2019)", "ford-kuga-mk2", 2012, 2019), 130, [
                new EngineVersion { EngineName = "1.5 EcoBoost 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1499, FuelTypeId = ben,
                    TorqueNm = 240, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.7m, TopSpeedKmh = 196, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "2.0 EcoBoost 242 KM", PowerHP = 242, PowerKW = 178, Displacement = 1999, FuelTypeId = ben,
                    TorqueNm = 340, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 220, FuelConsumptionCombined = 8.8m },
                new EngineVersion { EngineName = "2.0 TDCi 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 370, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.5m, TopSpeedKmh = 196, FuelConsumptionCombined = 5.3m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(kuga, "Mk3 (2019–)", "ford-kuga-mk3", 2019, null), 130, [
                new EngineVersion { EngineName = "1.5 EcoBoost 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1499, FuelTypeId = mild,
                    TorqueNm = 240, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.8m, TopSpeedKmh = 195, FuelConsumptionCombined = 6.7m },
                new EngineVersion { EngineName = "2.5 PHEV 225 KM", PowerHP = 225, PowerKW = 165, Displacement = 2488, FuelTypeId = phev,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.2m, TopSpeedKmh = 200, FuelConsumptionCombined = 1.4m },
                new EngineVersion { EngineName = "2.0 EcoBlue 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 370, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 10.0m, TopSpeedKmh = 198, FuelConsumptionCombined = 5.3m },
            ]);

            // ── HYUNDAI TUCSON III/IV + KONA I ────────────────────────────────────────
            int hyundaiId2 = GetOrCreateBrand("Hyundai", "hyundai", "auta-osobowe");
            int tucson = GetOrCreateModel(hyundaiId2, "Tucson", "hyundai-tucson");
            AddEngines(GetOrCreateGeneration(tucson, "III (2015–2020)", "hyundai-tucson-iii", 2015, 2020), [
                new EngineVersion { EngineName = "1.6 T-GDI 132 KM", PowerHP = 132, PowerKW = 97, Displacement = 1591, FuelTypeId = ben,
                    TorqueNm = 265, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.0m, TopSpeedKmh = 188, FuelConsumptionCombined = 7.3m },
                new EngineVersion { EngineName = "1.6 T-GDI 177 KM", PowerHP = 177, PowerKW = 130, Displacement = 1591, FuelTypeId = ben,
                    TorqueNm = 265, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 205, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "2.0 CRDi 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1995, FuelTypeId = die,
                    TorqueNm = 373, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 10.2m, TopSpeedKmh = 190, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "2.0 CRDi 185 KM", PowerHP = 185, PowerKW = 136, Displacement = 1995, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 208, FuelConsumptionCombined = 6.3m },
            ]);
            AddEngines(GetOrCreateGeneration(tucson, "IV (2020–)", "hyundai-tucson-iv", 2020, null), [
                new EngineVersion { EngineName = "1.6 T-GDI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1591, FuelTypeId = mild,
                    TorqueNm = 253, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.3m, TopSpeedKmh = 191, FuelConsumptionCombined = 7.2m },
                new EngineVersion { EngineName = "1.6 T-GDI Hybrid 230 KM", PowerHP = 230, PowerKW = 169, Displacement = 1591, FuelTypeId = hyb,
                    TorqueNm = 265, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 193, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "1.6 CRDi 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1598, FuelTypeId = mild,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 11.4m, TopSpeedKmh = 193, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "PHEV 265 KM", PowerHP = 265, PowerKW = 195, Displacement = 1591, FuelTypeId = phev,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.3m, TopSpeedKmh = 191, FuelConsumptionCombined = 1.3m },
            ]);
            int kona = GetOrCreateModel(hyundaiId2, "Kona", "hyundai-kona");
            AddEngines(GetOrCreateGeneration(kona, "I (2017–)", "hyundai-kona-i", 2017, null), [
                new EngineVersion { EngineName = "1.0 T-GDI 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 998, FuelTypeId = ben,
                    TorqueNm = 172, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.0m, TopSpeedKmh = 183, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "1.6 T-GDI 177 KM", PowerHP = 177, PowerKW = 130, Displacement = 1591, FuelTypeId = ben,
                    TorqueNm = 265, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.9m, TopSpeedKmh = 210, FuelConsumptionCombined = 8.3m },
                new EngineVersion { EngineName = "1.6 CRDi 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 280, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 183, FuelConsumptionCombined = 4.5m },
            ]);

            // ── KIA SPORTAGE IV + V ───────────────────────────────────────────────────
            int kiaId2 = GetOrCreateBrand("Kia", "kia", "auta-osobowe");
            int sportage = GetOrCreateModel(kiaId2, "Sportage", "kia-sportage");
            AddEngines(GetOrCreateGeneration(sportage, "IV (2015–2021)", "kia-sportage-iv", 2015, 2021), [
                new EngineVersion { EngineName = "1.6 T-GDI 132 KM", PowerHP = 132, PowerKW = 97, Displacement = 1591, FuelTypeId = ben,
                    TorqueNm = 265, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.3m, TopSpeedKmh = 188, FuelConsumptionCombined = 7.3m },
                new EngineVersion { EngineName = "2.0 CRDi 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1999, FuelTypeId = die,
                    TorqueNm = 373, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 10.2m, TopSpeedKmh = 190, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "2.0 CRDi 185 KM", PowerHP = 185, PowerKW = 136, Displacement = 1999, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.3m, TopSpeedKmh = 208, FuelConsumptionCombined = 6.3m },
            ]);
            AddEngines(GetOrCreateGeneration(sportage, "V (2021–)", "kia-sportage-v", 2021, null), [
                new EngineVersion { EngineName = "1.6 T-GDI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1591, FuelTypeId = mild,
                    TorqueNm = 253, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.3m, TopSpeedKmh = 192, FuelConsumptionCombined = 7.2m },
                new EngineVersion { EngineName = "1.6 T-GDI HEV 230 KM", PowerHP = 230, PowerKW = 169, Displacement = 1591, FuelTypeId = hyb,
                    TorqueNm = 265, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.6m, TopSpeedKmh = 193, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "1.6 CRDI 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1598, FuelTypeId = mild,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 11.4m, TopSpeedKmh = 193, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "PHEV 265 KM", PowerHP = 265, PowerKW = 195, Displacement = 1591, FuelTypeId = phev,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.3m, TopSpeedKmh = 191, FuelConsumptionCombined = 1.3m },
            ]);

            // ── RENAULT CLIO IV + V ───────────────────────────────────────────────────
            int renaultId2 = GetOrCreateBrand("Renault", "renault", "auta-osobowe");
            int clio = GetOrCreateModel(renaultId2, "Clio", "renault-clio");
            AddEngines(GetOrCreateGeneration(clio, "IV (2012–2019)", "renault-clio-iv", 2012, 2019), [
                new EngineVersion { EngineName = "0.9 TCe 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 898, FuelTypeId = ben,
                    TorqueNm = 135, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.5m, TopSpeedKmh = 181, FuelConsumptionCombined = 5.7m },
                new EngineVersion { EngineName = "1.2 TCe 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1197, FuelTypeId = ben,
                    TorqueNm = 190, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.0m, TopSpeedKmh = 200, FuelConsumptionCombined = 6.1m },
                new EngineVersion { EngineName = "RS 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 1618, FuelTypeId = ben,
                    TorqueNm = 240, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.7m, TopSpeedKmh = 235, FuelConsumptionCombined = 7.3m },
                new EngineVersion { EngineName = "1.5 dCi 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 220, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.0m, TopSpeedKmh = 181, FuelConsumptionCombined = 3.5m },
            ]);
            AddEngines(GetOrCreateGeneration(clio, "V (2019–)", "renault-clio-v", 2019, null), [
                new EngineVersion { EngineName = "1.0 TCe 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 160, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 12.7m, TopSpeedKmh = 178, FuelConsumptionCombined = 5.7m },
                new EngineVersion { EngineName = "1.3 TCe 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1332, FuelTypeId = ben,
                    TorqueNm = 240, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.0m, TopSpeedKmh = 205, FuelConsumptionCombined = 6.1m },
                new EngineVersion { EngineName = "E-Tech HEV 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1598, FuelTypeId = hyb,
                    TorqueNm = 205, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 180, FuelConsumptionCombined = 4.2m },
                new EngineVersion { EngineName = "1.5 dCi 85 KM", PowerHP = 85, PowerKW = 63, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.0m, TopSpeedKmh = 178, FuelConsumptionCombined = 3.5m },
            ]);

            int captur = GetOrCreateModel(renaultId2, "Captur", "renault-captur");
            PrepareGenerations(captur,
                ("I (2013–2019)", "renault-captur-i", 2013, 2019),
                ("II (2019–)", "renault-captur-ii", 2019, null));
            AddOrReplaceEngines(GetOrFixGeneration(captur, "I (2013–2019)", "renault-captur-i", 2013, 2019), 90, [
                new EngineVersion { EngineName = "0.9 TCe 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 898, FuelTypeId = ben,
                    TorqueNm = 135, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 12.0m, TopSpeedKmh = 174, FuelConsumptionCombined = 5.7m },
                new EngineVersion { EngineName = "1.5 dCi 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 220, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.6m, TopSpeedKmh = 174, FuelConsumptionCombined = 3.6m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(captur, "II (2019–)", "renault-captur-ii", 2019, null), 100, [
                new EngineVersion { EngineName = "1.0 TCe 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 160, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.8m, TopSpeedKmh = 173, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "E-Tech PHEV 160 KM", PowerHP = 160, PowerKW = 118, Displacement = 1598, FuelTypeId = phev,
                    TorqueNm = 205, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.1m, TopSpeedKmh = 175, FuelConsumptionCombined = 1.5m },
                new EngineVersion { EngineName = "1.5 dCi 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 260, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.9m, TopSpeedKmh = 179, FuelConsumptionCombined = 4.0m },
            ]);

            int kadjar = GetOrCreateModel(renaultId2, "Kadjar", "renault-kadjar");
            AddOrReplaceEngines(GetOrFixGeneration(kadjar, "I (2015–2022)", "renault-kadjar-i", 2015, 2022), 100, [
                new EngineVersion { EngineName = "1.3 TCe 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1332, FuelTypeId = ben,
                    TorqueNm = 240, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.7m, TopSpeedKmh = 200, FuelConsumptionCombined = 6.3m },
                new EngineVersion { EngineName = "1.5 dCi 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 260, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.1m, TopSpeedKmh = 187, FuelConsumptionCombined = 4.2m },
            ]);

            int scenic = GetOrCreateModel(renaultId2, "Scenic", "renault-scenic");
            AddOrReplaceEngines(GetOrFixGeneration(scenic, "IV (2016–2023)", "renault-scenic-iv", 2016, 2023), 110, [
                new EngineVersion { EngineName = "1.3 TCe 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1332, FuelTypeId = ben,
                    TorqueNm = 240, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 200, FuelConsumptionCombined = 6.7m },
                new EngineVersion { EngineName = "1.5 dCi 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 260, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 188, FuelConsumptionCombined = 4.3m },
            ]);

            int twingo = GetOrCreateModel(renaultId2, "Twingo", "renault-twingo");
            AddOrReplaceEngines(GetOrFixGeneration(twingo, "III (2014–)", "renault-twingo-iii", 2014, null), 65, [
                new EngineVersion { EngineName = "1.0 SCe 70 KM", PowerHP = 70, PowerKW = 51, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 91, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, Acceleration0100 = 14.5m, TopSpeedKmh = 156, FuelConsumptionCombined = 4.6m },
                new EngineVersion { EngineName = "Z.E. Electric 82 KM", PowerHP = 82, PowerKW = 60, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 160, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "RWD",
                    Cylinders = 0, Acceleration0100 = 12.9m, TopSpeedKmh = 135, FuelConsumptionCombined = 16.1m },
            ]);

            int zoe = GetOrCreateModel(renaultId2, "Zoe", "renault-zoe");
            AddOrReplaceEngines(GetOrFixGeneration(zoe, "II (2019–)", "renault-zoe-ii", 2019, null), 100, [
                new EngineVersion { EngineName = "Z.E.50 52 kWh 135 KM", PowerHP = 135, PowerKW = 100, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 245, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 0, Acceleration0100 = 9.5m, TopSpeedKmh = 140, FuelConsumptionCombined = 17.2m },
            ]);

            int austral = GetOrCreateModel(renaultId2, "Austral", "renault-austral");
            AddOrReplaceEngines(GetOrFixGeneration(austral, "I (2022–)", "renault-austral-i", 2022, null), 140, [
                new EngineVersion { EngineName = "E-Tech Hybrid 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 1199, FuelTypeId = hyb,
                    TorqueNm = 205, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 8.4m, TopSpeedKmh = 175, FuelConsumptionCombined = 4.9m },
                new EngineVersion { EngineName = "TCe 140 KM Mild Hybrid", PowerHP = 140, PowerKW = 103, Displacement = 1332, FuelTypeId = mild,
                    TorqueNm = 240, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.4m, TopSpeedKmh = 195, FuelConsumptionCombined = 6.4m },
            ]);

            // ── OPEL CORSA E/F + INSIGNIA B ──────────────────────────────────────────
            int opelId2 = GetOrCreateBrand("Opel", "opel", "auta-osobowe");
            int corsa = GetOrCreateModel(opelId2, "Corsa", "opel-corsa");
            AddEngines(GetOrCreateGeneration(corsa, "E (2014–2019)", "opel-corsa-e", 2014, 2019), [
                new EngineVersion { EngineName = "1.0 Turbo 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 170, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.5m, TopSpeedKmh = 178, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "1.0 Turbo 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.9m, TopSpeedKmh = 198, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "OPC 1.6 Turbo 207 KM", PowerHP = 207, PowerKW = 152, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 280, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.8m, TopSpeedKmh = 240, FuelConsumptionCombined = 7.9m },
                new EngineVersion { EngineName = "1.3 CDTi 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 1248, FuelTypeId = die,
                    TorqueNm = 190, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.5m, TopSpeedKmh = 182, FuelConsumptionCombined = 3.7m },
            ]);
            AddEngines(GetOrCreateGeneration(corsa, "F (2019–)", "opel-corsa-f", 2019, null), [
                new EngineVersion { EngineName = "1.2 75 KM", PowerHP = 75, PowerKW = 55, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 118, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 14.5m, TopSpeedKmh = 165, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "1.2 Turbo 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 205, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.7m, TopSpeedKmh = 188, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "1.2 Turbo 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 8.8m, TopSpeedKmh = 208, FuelConsumptionCombined = 5.6m },
            ]);
            int insignia = GetOrCreateModel(opelId2, "Insignia", "opel-insignia");
            AddEngines(GetOrCreateGeneration(insignia, "B (2017–)", "opel-insignia-b", 2017, null), [
                new EngineVersion { EngineName = "1.5 Turbo 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1490, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 210, FuelConsumptionCombined = 7.1m },
                new EngineVersion { EngineName = "2.0 Turbo 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 230, FuelConsumptionCombined = 8.3m },
                new EngineVersion { EngineName = "GSi 260 KM", PowerHP = 260, PowerKW = 191, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.3m, TopSpeedKmh = 250, FuelConsumptionCombined = 9.0m },
                new EngineVersion { EngineName = "2.0 CDTi 170 KM", PowerHP = 170, PowerKW = 125, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.4m, TopSpeedKmh = 220, FuelConsumptionCombined = 5.4m },
            ]);

            int mokka = GetOrCreateModel(opelId2, "Mokka", "opel-mokka");
            PrepareGenerations(mokka,
                ("I (2012–2019)", "opel-mokka-i", 2012, 2019),
                ("II (2020–)", "opel-mokka-ii", 2020, null));
            AddOrReplaceEngines(GetOrFixGeneration(mokka, "I (2012–2019)", "opel-mokka-i", 2012, 2019), 100, [
                new EngineVersion { EngineName = "1.4 Turbo 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1364, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 189, FuelConsumptionCombined = 6.8m },
                new EngineVersion { EngineName = "1.6 CDTi 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 190, FuelConsumptionCombined = 5.2m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(mokka, "II (2020–)", "opel-mokka-ii", 2020, null), 100, [
                new EngineVersion { EngineName = "1.2 Turbo 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.2m, TopSpeedKmh = 202, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "Mokka-e 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 260, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 0, Acceleration0100 = 9.1m, TopSpeedKmh = 150, FuelConsumptionCombined = 16.4m },
            ]);

            int crossland = GetOrCreateModel(opelId2, "Crossland", "opel-crossland");
            AddOrReplaceEngines(GetOrFixGeneration(crossland, "I (2017–)", "opel-crossland-i", 2017, null), 90, [
                new EngineVersion { EngineName = "1.2 Turbo 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 205, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.3m, TopSpeedKmh = 188, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "1.5 Diesel 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.4m, TopSpeedKmh = 180, FuelConsumptionCombined = 4.2m },
            ]);

            int grandland = GetOrCreateModel(opelId2, "Grandland", "opel-grandland");
            AddOrReplaceEngines(GetOrFixGeneration(grandland, "I (2017–)", "opel-grandland-i", 2017, null), 130, [
                new EngineVersion { EngineName = "1.2 Turbo 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.0m, TopSpeedKmh = 195, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "Hybrid4 300 KM PHEV AWD", PowerHP = 300, PowerKW = 221, Displacement = 1598, FuelTypeId = phev,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 5.9m, TopSpeedKmh = 235, FuelConsumptionCombined = 1.6m },
                new EngineVersion { EngineName = "1.5 Diesel 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.2m, TopSpeedKmh = 205, FuelConsumptionCombined = 4.7m },
            ]);

            int vivaro = GetOrCreateModel(opelId2, "Vivaro", "opel-vivaro");
            AddOrReplaceEngines(GetOrFixGeneration(vivaro, "C (2019–)", "opel-vivaro-c", 2019, null), 100, [
                new EngineVersion { EngineName = "2.0 Diesel 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.0m, TopSpeedKmh = 174, FuelConsumptionCombined = 6.1m },
                new EngineVersion { EngineName = "2.0 Diesel 177 KM", PowerHP = 177, PowerKW = 130, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 187, FuelConsumptionCombined = 6.4m },
            ]);

            // ── MITSUBISHI OUTLANDER III + IV ─────────────────────────────────────────
            int mitId = GetOrCreateBrand("Mitsubishi", "mitsubishi", "auta-osobowe");
            int outlander = GetOrCreateModel(mitId, "Outlander", "mitsubishi-outlander");
            AddEngines(GetOrCreateGeneration(outlander, "III (2012–2021)", "mitsubishi-outlander-iii", 2012, 2021), [
                new EngineVersion { EngineName = "2.0 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 196, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 192, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "2.2 Di-D 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 2268, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 190, FuelConsumptionCombined = 5.7m },
                new EngineVersion { EngineName = "PHEV 224 KM", PowerHP = 224, PowerKW = 165, Displacement = 2360, FuelTypeId = phev,
                    TorqueNm = 332, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 170, FuelConsumptionCombined = 1.9m },
            ]);
            AddEngines(GetOrCreateGeneration(outlander, "IV (2021–)", "mitsubishi-outlander-iv", 2021, null), [
                new EngineVersion { EngineName = "PHEV 2.4 302 KM", PowerHP = 302, PowerKW = 222, Displacement = 2360, FuelTypeId = phev,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.8m, TopSpeedKmh = 200, FuelConsumptionCombined = 1.8m },
            ]);

            // ── MITSUBISHI ASX I + II + ECLIPSE CROSS + L200 V/VI ──────────────────
            int mitId2 = GetOrCreateBrand("Mitsubishi", "mitsubishi", "auta-osobowe");
            int asx = GetOrCreateModel(mitId2, "ASX", "mitsubishi-asx");
            AddEngines(GetOrCreateGeneration(asx, "I (2010–2022)", "mitsubishi-asx-i", 2010, 2022), [
                new EngineVersion { EngineName = "1.6 117 KM", PowerHP = 117, PowerKW = 86, Displacement = 1590, FuelTypeId = ben,
                    TorqueNm = 154, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 186, FuelConsumptionCombined = 7.2m },
                new EngineVersion { EngineName = "2.0 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 196, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 10.0m, TopSpeedKmh = 190, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "1.8 Di-D 116 KM", PowerHP = 116, PowerKW = 85, Displacement = 1798, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 182, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "2.2 Di-D 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 2268, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 10.8m, TopSpeedKmh = 188, FuelConsumptionCombined = 6.0m },
            ]);
            AddEngines(GetOrCreateGeneration(asx, "II (2022–)", "mitsubishi-asx-ii", 2022, null), [
                new EngineVersion { EngineName = "1.0 Mild Hybrid 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 999, FuelTypeId = mild,
                    TorqueNm = 160, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 12.5m, TopSpeedKmh = 178, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "1.3 Mild Hybrid 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1332, FuelTypeId = mild,
                    TorqueNm = 270, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 195, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "PHEV 1.6 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 1598, FuelTypeId = phev,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 190, FuelConsumptionCombined = 1.4m },
            ]);
            int eclipseCross = GetOrCreateModel(mitId2, "Eclipse Cross", "mitsubishi-eclipse-cross");
            AddEngines(GetOrCreateGeneration(eclipseCross, "GK (2017–)", "mitsubishi-eclipse-cross-gk", 2017, null), [
                new EngineVersion { EngineName = "1.5T 163 KM", PowerHP = 163, PowerKW = 120, Displacement = 1499, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.9m, TopSpeedKmh = 198, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "PHEV 2.4 188 KM", PowerHP = 188, PowerKW = 138, Displacement = 2360, FuelTypeId = phev,
                    TorqueNm = 332, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 190, FuelConsumptionCombined = 1.9m },
            ]);
            int l200 = GetOrCreateModel(mitId2, "L200", "mitsubishi-l200");
            AddEngines(GetOrCreateGeneration(l200, "V (2014–2019)", "mitsubishi-l200-v", 2014, 2019), [
                new EngineVersion { EngineName = "2.4 Di-D 154 KM", PowerHP = 154, PowerKW = 113, Displacement = 2442, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 11.8m, TopSpeedKmh = 175, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "2.4 Di-D 178 KM", PowerHP = 178, PowerKW = 131, Displacement = 2442, FuelTypeId = die,
                    TorqueNm = 430, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 180, FuelConsumptionCombined = 8.2m },
            ]);
            AddEngines(GetOrCreateGeneration(l200, "VI (2019–)", "mitsubishi-l200-vi", 2019, null), [
                new EngineVersion { EngineName = "2.2 Di-D 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 2268, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 175, FuelConsumptionCombined = 7.8m },
            ]);

            int spaceStar = GetOrCreateModel(mitId2, "Space Star", "mitsubishi-space-star");
            AddOrReplaceEngines(GetOrFixGeneration(spaceStar, "II (2013–)", "mitsubishi-space-star-ii", 2013, null), 70, [
                new EngineVersion { EngineName = "1.0 71 KM", PowerHP = 71, PowerKW = 52, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 92, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 15.0m, TopSpeedKmh = 158, FuelConsumptionCombined = 4.9m },
                new EngineVersion { EngineName = "1.2 80 KM", PowerHP = 80, PowerKW = 59, Displacement = 1193, FuelTypeId = ben,
                    TorqueNm = 110, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 12.6m, TopSpeedKmh = 167, FuelConsumptionCombined = 5.0m },
            ]);

            int colt = GetOrCreateModel(mitId2, "Colt", "mitsubishi-colt");
            AddOrReplaceEngines(GetOrFixGeneration(colt, "IV (2024–)", "mitsubishi-colt-iv", 2024, null), 90, [
                new EngineVersion { EngineName = "1.0 T 91 KM", PowerHP = 91, PowerKW = 67, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 160, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.7m, TopSpeedKmh = 173, FuelConsumptionCombined = 5.3m },
                new EngineVersion { EngineName = "e-Colt 122 KM", PowerHP = 122, PowerKW = 90, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 270, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 0, Acceleration0100 = 9.6m, TopSpeedKmh = 150, FuelConsumptionCombined = 15.5m },
            ]);

            int pajero = GetOrCreateModel(mitId2, "Pajero", "mitsubishi-pajero");
            AddOrReplaceEngines(GetOrFixGeneration(pajero, "IV (2006–2021)", "mitsubishi-pajero-iv", 2006, 2021), 150, [
                new EngineVersion { EngineName = "3.2 Di-D 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 3200, FuelTypeId = die,
                    TorqueNm = 441, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 10.7m, TopSpeedKmh = 180, FuelConsumptionCombined = 9.0m },
                new EngineVersion { EngineName = "3.8 V6 250 KM", PowerHP = 250, PowerKW = 184, Displacement = 3828, FuelTypeId = ben,
                    TorqueNm = 343, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 10.3m, TopSpeedKmh = 180, FuelConsumptionCombined = 13.5m },
            ]);

            int lancer = GetOrCreateModel(mitId2, "Lancer", "mitsubishi-lancer");
            AddOrReplaceEngines(GetOrFixGeneration(lancer, "X (2007–2017)", "mitsubishi-lancer-x", 2007, 2017), 120, [
                new EngineVersion { EngineName = "1.5 109 KM", PowerHP = 109, PowerKW = 80, Displacement = 1499, FuelTypeId = ben,
                    TorqueNm = 141, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.6m, TopSpeedKmh = 185, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "Evolution X 2.0T 295 KM", PowerHP = 295, PowerKW = 217, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 366, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 4.7m, TopSpeedKmh = 240, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "1.8 DI-D 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1798, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.6m, TopSpeedKmh = 200, FuelConsumptionCombined = 4.7m },
            ]);

            // ── TOYOTA CAMRY XV70 + RAV4 IV ──────────────────────────────────────
            int toyotaId3 = GetOrCreateBrand("Toyota", "toyota", "auta-osobowe");
            int camry = GetOrCreateModel(toyotaId3, "Camry", "toyota-camry");
            AddEngines(GetOrCreateGeneration(camry, "XV70 (2017–)", "toyota-camry-xv70", 2017, null), [
                new EngineVersion { EngineName = "2.0 VVT-i 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1987, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.5m, TopSpeedKmh = 203, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "2.5 HEV 218 KM", PowerHP = 218, PowerKW = 160, Displacement = 2487, FuelTypeId = hyb,
                    TorqueNm = 221, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.3m, TopSpeedKmh = 180, FuelConsumptionCombined = 5.0m },
            ]);
            int rav4b = GetOrCreateModel(toyotaId3, "RAV4", "toyota-rav4");
            AddEngines(GetOrCreateGeneration(rav4b, "IV (2012–2018)", "toyota-rav4-iv", 2012, 2018), [
                new EngineVersion { EngineName = "2.0 VVT-i 152 KM", PowerHP = 152, PowerKW = 112, Displacement = 1987, FuelTypeId = ben,
                    TorqueNm = 193, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.2m, TopSpeedKmh = 185, FuelConsumptionCombined = 7.8m },
                new EngineVersion { EngineName = "2.5 HEV 197 KM", PowerHP = 197, PowerKW = 145, Displacement = 2494, FuelTypeId = hyb,
                    TorqueNm = 210, EuroNorm = "Euro 6", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.3m, TopSpeedKmh = 180, FuelConsumptionCombined = 5.2m },
                new EngineVersion { EngineName = "2.0 D-4D 124 KM", PowerHP = 124, PowerKW = 91, Displacement = 1998, FuelTypeId = die,
                    TorqueNm = 310, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 180, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "2.2 D-4D 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 2231, FuelTypeId = die,
                    TorqueNm = 340, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 185, FuelConsumptionCombined = 6.0m },
            ]);

            // ── FORD FOCUS MK3 + MK4 ─────────────────────────────────────────────
            int fordId3 = GetOrCreateBrand("Ford", "ford", "auta-osobowe");
            int focus = GetOrCreateModel(fordId3, "Focus", "ford-focus");
            AddEngines(GetOrCreateGeneration(focus, "Mk3 (2011–2018)", "ford-focus-mk3", 2011, 2018), [
                new EngineVersion { EngineName = "1.0 EcoBoost 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 170, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.5m, TopSpeedKmh = 192, FuelConsumptionCombined = 5.2m },
                new EngineVersion { EngineName = "1.0 EcoBoost 125 KM", PowerHP = 125, PowerKW = 92, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 170, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.8m, TopSpeedKmh = 203, FuelConsumptionCombined = 5.2m },
                new EngineVersion { EngineName = "1.5 EcoBoost 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1499, FuelTypeId = ben,
                    TorqueNm = 240, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.8m, TopSpeedKmh = 215, FuelConsumptionCombined = 5.8m },
                new EngineVersion { EngineName = "2.0 ST 250 KM", PowerHP = 250, PowerKW = 184, Displacement = 1999, FuelTypeId = ben,
                    TorqueNm = 360, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.5m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "RS 2.3 350 KM", PowerHP = 350, PowerKW = 257, Displacement = 2261, FuelTypeId = ben,
                    TorqueNm = 470, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 4.7m, TopSpeedKmh = 266, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "1.5 TDCi 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 230, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.5m, TopSpeedKmh = 187, FuelConsumptionCombined = 4.1m },
                new EngineVersion { EngineName = "1.5 TDCi 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 270, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 200, FuelConsumptionCombined = 4.2m },
                new EngineVersion { EngineName = "2.0 TDCi 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 370, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.5m, TopSpeedKmh = 210, FuelConsumptionCombined = 4.8m },
            ]);
            AddEngines(GetOrCreateGeneration(focus, "Mk4 (2018–)", "ford-focus-mk4", 2018, null), [
                new EngineVersion { EngineName = "1.0 EcoBoost 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 999, FuelTypeId = mild,
                    TorqueNm = 170, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.2m, TopSpeedKmh = 192, FuelConsumptionCombined = 5.0m },
                new EngineVersion { EngineName = "1.0 EcoBoost 125 KM", PowerHP = 125, PowerKW = 92, Displacement = 999, FuelTypeId = mild,
                    TorqueNm = 170, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.5m, TopSpeedKmh = 200, FuelConsumptionCombined = 5.0m },
                new EngineVersion { EngineName = "1.5 EcoBoost 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1499, FuelTypeId = mild,
                    TorqueNm = 240, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 8.5m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "2.3 ST 280 KM", PowerHP = 280, PowerKW = 206, Displacement = 2261, FuelTypeId = ben,
                    TorqueNm = 420, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 5.8m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.8m },
                new EngineVersion { EngineName = "1.5 EcoBlue 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 230, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.5m, TopSpeedKmh = 187, FuelConsumptionCombined = 4.0m },
                new EngineVersion { EngineName = "1.5 EcoBlue 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 270, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.2m, TopSpeedKmh = 200, FuelConsumptionCombined = 4.1m },
                new EngineVersion { EngineName = "2.0 EcoBlue 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 370, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.2m, TopSpeedKmh = 210, FuelConsumptionCombined = 4.5m },
            ]);

            // ── SUBARU FORESTER V + OUTBACK VI + IMPREZA V + XV II + WRX STI IV ─
            int subaruId = GetOrCreateBrand("Subaru", "subaru", "auta-osobowe");
            int forester = GetOrCreateModel(subaruId, "Forester", "subaru-forester");
            AddEngines(GetOrCreateGeneration(forester, "V (2018–)", "subaru-forester-v", 2018, null), [
                new EngineVersion { EngineName = "2.0i e-BOXER 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1995, FuelTypeId = hyb,
                    TorqueNm = 196, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 188, FuelConsumptionCombined = 6.7m },
                new EngineVersion { EngineName = "2.5i 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 2498, FuelTypeId = ben,
                    TorqueNm = 241, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.2m, TopSpeedKmh = 200, FuelConsumptionCombined = 8.5m },
            ]);
            int outback = GetOrCreateModel(subaruId, "Outback", "subaru-outback");
            AddEngines(GetOrCreateGeneration(outback, "VI (2020–)", "subaru-outback-vi", 2020, null), [
                new EngineVersion { EngineName = "2.5i 169 KM", PowerHP = 169, PowerKW = 124, Displacement = 2498, FuelTypeId = ben,
                    TorqueNm = 252, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.0m, TopSpeedKmh = 195, FuelConsumptionCombined = 8.8m },
                new EngineVersion { EngineName = "2.5i e-BOXER 174 KM", PowerHP = 174, PowerKW = 128, Displacement = 2498, FuelTypeId = hyb,
                    TorqueNm = 252, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.8m, TopSpeedKmh = 200, FuelConsumptionCombined = 7.5m },
            ]);
            int impreza = GetOrCreateModel(subaruId, "Impreza", "subaru-impreza");
            AddEngines(GetOrCreateGeneration(impreza, "V (2016–)", "subaru-impreza-v", 2016, null), [
                new EngineVersion { EngineName = "2.0i 156 KM", PowerHP = 156, PowerKW = 115, Displacement = 1995, FuelTypeId = ben,
                    TorqueNm = 196, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 10.0m, TopSpeedKmh = 195, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "2.0i e-BOXER 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1995, FuelTypeId = hyb,
                    TorqueNm = 196, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 185, FuelConsumptionCombined = 6.5m },
            ]);
            int xv = GetOrCreateModel(subaruId, "XV", "subaru-xv");
            AddEngines(GetOrCreateGeneration(xv, "II (2017–)", "subaru-xv-ii", 2017, null), [
                new EngineVersion { EngineName = "2.0i 156 KM", PowerHP = 156, PowerKW = 115, Displacement = 1995, FuelTypeId = ben,
                    TorqueNm = 196, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 192, FuelConsumptionCombined = 7.8m },
                new EngineVersion { EngineName = "2.0i e-BOXER 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1995, FuelTypeId = hyb,
                    TorqueNm = 196, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 188, FuelConsumptionCombined = 6.3m },
            ]);
            int wrxSti = GetOrCreateModel(subaruId, "WRX STI", "subaru-wrx-sti");
            AddEngines(GetOrCreateGeneration(wrxSti, "IV (2014–2021)", "subaru-wrx-sti-iv", 2014, 2021), [
                new EngineVersion { EngineName = "2.0 DIT 268 KM", PowerHP = 268, PowerKW = 197, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.4m, TopSpeedKmh = 225, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "STI 2.5T 304 KM", PowerHP = 304, PowerKW = 224, Displacement = 2457, FuelTypeId = ben,
                    TorqueNm = 407, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 5.2m, TopSpeedKmh = 255, FuelConsumptionCombined = 11.5m },
            ]);

            // ── MAN TGX I + NEO + TGS I + II ─────────────────────────────────────
            int manId = GetOrCreateBrand("MAN", "man", "ciezarowki");
            int tgx = GetOrCreateModel(manId, "TGX", "man-tgx");
            AddEngines(GetOrCreateGeneration(tgx, "I (2007–2020)", "man-tgx-i", 2007, 2020), [
                new EngineVersion { EngineName = "D2066 400 KM", PowerHP = 400, PowerKW = 294, Displacement = 10518, FuelTypeId = die,
                    TorqueNm = 1900, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 28.0m },
                new EngineVersion { EngineName = "D2066 440 KM", PowerHP = 440, PowerKW = 324, Displacement = 10518, FuelTypeId = die,
                    TorqueNm = 2100, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 28.0m },
                new EngineVersion { EngineName = "D2676 480 KM", PowerHP = 480, PowerKW = 353, Displacement = 12419, FuelTypeId = die,
                    TorqueNm = 2300, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 28.0m },
                new EngineVersion { EngineName = "D2676 520 KM", PowerHP = 520, PowerKW = 382, Displacement = 12419, FuelTypeId = die,
                    TorqueNm = 2500, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 28.0m },
            ]);
            AddEngines(GetOrCreateGeneration(tgx, "NEO (2020–)", "man-tgx-neo", 2020, null), [
                new EngineVersion { EngineName = "D2676 400 KM", PowerHP = 400, PowerKW = 294, Displacement = 12419, FuelTypeId = die,
                    TorqueNm = 1900, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 27.0m },
                new EngineVersion { EngineName = "D2676 430 KM", PowerHP = 430, PowerKW = 316, Displacement = 12419, FuelTypeId = die,
                    TorqueNm = 2100, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 27.0m },
                new EngineVersion { EngineName = "D2676 470 KM", PowerHP = 470, PowerKW = 346, Displacement = 12419, FuelTypeId = die,
                    TorqueNm = 2300, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 27.0m },
                new EngineVersion { EngineName = "D3876 530 KM", PowerHP = 530, PowerKW = 390, Displacement = 15249, FuelTypeId = die,
                    TorqueNm = 2600, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 27.0m },
            ]);
            int tgs = GetOrCreateModel(manId, "TGS", "man-tgs");
            AddEngines(GetOrCreateGeneration(tgs, "I (2007–2020)", "man-tgs-i", 2007, 2020), [
                new EngineVersion { EngineName = "D2066 320 KM", PowerHP = 320, PowerKW = 235, Displacement = 10518, FuelTypeId = die,
                    TorqueNm = 1550, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 26.0m },
                new EngineVersion { EngineName = "D2066 360 KM", PowerHP = 360, PowerKW = 265, Displacement = 10518, FuelTypeId = die,
                    TorqueNm = 1800, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 26.0m },
                new EngineVersion { EngineName = "D2676 400 KM", PowerHP = 400, PowerKW = 294, Displacement = 12419, FuelTypeId = die,
                    TorqueNm = 1900, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 28.0m },
                new EngineVersion { EngineName = "D2676 440 KM", PowerHP = 440, PowerKW = 324, Displacement = 12419, FuelTypeId = die,
                    TorqueNm = 2100, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 28.0m },
            ]);
            AddEngines(GetOrCreateGeneration(tgs, "II (2020–)", "man-tgs-ii", 2020, null), [
                new EngineVersion { EngineName = "D2676 330 KM", PowerHP = 330, PowerKW = 243, Displacement = 12419, FuelTypeId = die,
                    TorqueNm = 1600, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 25.0m },
                new EngineVersion { EngineName = "D2676 380 KM", PowerHP = 380, PowerKW = 279, Displacement = 12419, FuelTypeId = die,
                    TorqueNm = 1900, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 25.0m },
                new EngineVersion { EngineName = "D2676 430 KM", PowerHP = 430, PowerKW = 316, Displacement = 12419, FuelTypeId = die,
                    TorqueNm = 2100, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 25.0m },
            ]);

            // ── SCANIA R-SERIES + S-SERIES ────────────────────────────────────────
            int scaniaId = GetOrCreateBrand("Scania", "scania", "ciezarowki");
            int scaniaR = GetOrCreateModel(scaniaId, "R-Series", "scania-r-series");
            AddEngines(GetOrCreateGeneration(scaniaR, "R5 (2009–2016)", "scania-r-r5", 2009, 2016), [
                new EngineVersion { EngineName = "DC09 320 KM", PowerHP = 320, PowerKW = 235, Displacement = 9290, FuelTypeId = die,
                    TorqueNm = 1600, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 28.0m },
                new EngineVersion { EngineName = "DC13 420 KM", PowerHP = 420, PowerKW = 309, Displacement = 12742, FuelTypeId = die,
                    TorqueNm = 2100, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 28.0m },
                new EngineVersion { EngineName = "DC13 450 KM", PowerHP = 450, PowerKW = 331, Displacement = 12742, FuelTypeId = die,
                    TorqueNm = 2200, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 28.0m },
                new EngineVersion { EngineName = "DC16 580 KM", PowerHP = 580, PowerKW = 427, Displacement = 15607, FuelTypeId = die,
                    TorqueNm = 2950, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 30.0m },
            ]);
            AddEngines(GetOrCreateGeneration(scaniaR, "Next Gen (2016–)", "scania-r-nextgen", 2016, null), [
                new EngineVersion { EngineName = "DC09 320 KM", PowerHP = 320, PowerKW = 235, Displacement = 9290, FuelTypeId = die,
                    TorqueNm = 1600, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 27.0m },
                new EngineVersion { EngineName = "DC13 410 KM", PowerHP = 410, PowerKW = 302, Displacement = 12742, FuelTypeId = die,
                    TorqueNm = 2050, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 27.0m },
                new EngineVersion { EngineName = "DC13 460 KM", PowerHP = 460, PowerKW = 338, Displacement = 12742, FuelTypeId = die,
                    TorqueNm = 2350, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 27.0m },
                new EngineVersion { EngineName = "DC13 500 KM", PowerHP = 500, PowerKW = 368, Displacement = 12742, FuelTypeId = die,
                    TorqueNm = 2550, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 27.0m },
                new EngineVersion { EngineName = "DC16 590 KM", PowerHP = 590, PowerKW = 434, Displacement = 15607, FuelTypeId = die,
                    TorqueNm = 3000, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 29.0m },
            ]);
            int scaniaS = GetOrCreateModel(scaniaId, "S-Series", "scania-s-series");
            AddEngines(GetOrCreateGeneration(scaniaS, "Next Gen (2016–)", "scania-s-nextgen", 2016, null), [
                new EngineVersion { EngineName = "DC13 410 KM", PowerHP = 410, PowerKW = 302, Displacement = 12742, FuelTypeId = die,
                    TorqueNm = 2050, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 27.0m },
                new EngineVersion { EngineName = "DC13 460 KM", PowerHP = 460, PowerKW = 338, Displacement = 12742, FuelTypeId = die,
                    TorqueNm = 2350, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 27.0m },
                new EngineVersion { EngineName = "DC13 500 KM", PowerHP = 500, PowerKW = 368, Displacement = 12742, FuelTypeId = die,
                    TorqueNm = 2550, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 27.0m },
                new EngineVersion { EngineName = "DC16 590 KM", PowerHP = 590, PowerKW = 434, Displacement = 15607, FuelTypeId = die,
                    TorqueNm = 3000, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 89, FuelConsumptionCombined = 29.0m },
            ]);

            // ── PORSCHE 911/CAYENNE/MACAN/TAYCAN/PANAMERA ─────────────────────────
            int porscheId = GetOrCreateBrand("Porsche", "porsche", "auta-osobowe");
            int p911 = GetOrCreateModel(porscheId, "911", "porsche-911");
            AddEngines(GetOrCreateGeneration(p911, "991 (2011–2018)", "porsche-911-991", 2011, 2018), [
                new EngineVersion { EngineName = "3.0 T6 Carrera 370 KM", PowerHP = 370, PowerKW = 272, Displacement = 2981, FuelTypeId = ben,
                    TorqueNm = 450, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 4.8m, TopSpeedKmh = 289, FuelConsumptionCombined = 9.0m },
                new EngineVersion { EngineName = "3.0 T6 Carrera S 420 KM", PowerHP = 420, PowerKW = 309, Displacement = 2981, FuelTypeId = ben,
                    TorqueNm = 500, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 4.3m, TopSpeedKmh = 304, FuelConsumptionCombined = 9.5m },
                new EngineVersion { EngineName = "3.8 T6 Turbo S 580 KM", PowerHP = 580, PowerKW = 427, Displacement = 3800, FuelTypeId = ben,
                    TorqueNm = 750, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 3.0m, TopSpeedKmh = 318, FuelConsumptionCombined = 11.0m },
                new EngineVersion { EngineName = "GT3 4.0 500 KM", PowerHP = 500, PowerKW = 368, Displacement = 3996, FuelTypeId = ben,
                    TorqueNm = 460, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 3.8m, TopSpeedKmh = 318, FuelConsumptionCombined = 13.0m },
            ]);
            AddEngines(GetOrCreateGeneration(p911, "992 (2018–)", "porsche-911-992", 2018, null), [
                new EngineVersion { EngineName = "3.0 T6 Carrera 385 KM", PowerHP = 385, PowerKW = 283, Displacement = 2981, FuelTypeId = ben,
                    TorqueNm = 450, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 4.2m, TopSpeedKmh = 293, FuelConsumptionCombined = 9.5m },
                new EngineVersion { EngineName = "3.0 T6 Carrera S 450 KM", PowerHP = 450, PowerKW = 331, Displacement = 2981, FuelTypeId = ben,
                    TorqueNm = 530, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 3.7m, TopSpeedKmh = 308, FuelConsumptionCombined = 10.0m },
                new EngineVersion { EngineName = "3.8 T6 Turbo S 650 KM", PowerHP = 650, PowerKW = 478, Displacement = 3745, FuelTypeId = ben,
                    TorqueNm = 800, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 2.7m, TopSpeedKmh = 330, FuelConsumptionCombined = 11.5m },
                new EngineVersion { EngineName = "GT3 4.0 510 KM", PowerHP = 510, PowerKW = 375, Displacement = 3996, FuelTypeId = ben,
                    TorqueNm = 470, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 3.4m, TopSpeedKmh = 320, FuelConsumptionCombined = 13.0m },
            ]);
            int cayenne = GetOrCreateModel(porscheId, "Cayenne", "porsche-cayenne");
            AddEngines(GetOrCreateGeneration(cayenne, "9YA (2017–)", "porsche-cayenne-9ya", 2017, null), [
                new EngineVersion { EngineName = "3.0 T6 340 KM", PowerHP = 340, PowerKW = 250, Displacement = 2995, FuelTypeId = ben,
                    TorqueNm = 450, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.0m, TopSpeedKmh = 253, FuelConsumptionCombined = 10.0m },
                new EngineVersion { EngineName = "3.0 T6 S 440 KM", PowerHP = 440, PowerKW = 324, Displacement = 2995, FuelTypeId = ben,
                    TorqueNm = 550, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 4.8m, TopSpeedKmh = 270, FuelConsumptionCombined = 11.5m },
                new EngineVersion { EngineName = "4.0 V8 Turbo 550 KM", PowerHP = 550, PowerKW = 404, Displacement = 3996, FuelTypeId = ben,
                    TorqueNm = 770, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 3.9m, TopSpeedKmh = 286, FuelConsumptionCombined = 13.0m },
                new EngineVersion { EngineName = "4.0 V8 Turbo GT 640 KM", PowerHP = 640, PowerKW = 471, Displacement = 3996, FuelTypeId = ben,
                    TorqueNm = 870, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 3.3m, TopSpeedKmh = 300, FuelConsumptionCombined = 13.5m },
                new EngineVersion { EngineName = "E-Hybrid PHEV 462 KM", PowerHP = 462, PowerKW = 340, Displacement = 2995, FuelTypeId = phev,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 4.7m, TopSpeedKmh = 263, FuelConsumptionCombined = 3.0m },
            ]);
            int macan = GetOrCreateModel(porscheId, "Macan", "porsche-macan");
            AddEngines(GetOrCreateGeneration(macan, "95B (2013–2023)", "porsche-macan-95b", 2013, 2023), [
                new EngineVersion { EngineName = "2.0T 245 KM", PowerHP = 245, PowerKW = 180, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 370, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.7m, TopSpeedKmh = 229, FuelConsumptionCombined = 9.5m },
                new EngineVersion { EngineName = "3.0 T6 GTS 380 KM", PowerHP = 380, PowerKW = 279, Displacement = 2995, FuelTypeId = ben,
                    TorqueNm = 520, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 4.7m, TopSpeedKmh = 272, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "3.0 Turbo 440 KM", PowerHP = 440, PowerKW = 324, Displacement = 2995, FuelTypeId = ben,
                    TorqueNm = 600, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 4.3m, TopSpeedKmh = 283, FuelConsumptionCombined = 11.0m },
                new EngineVersion { EngineName = "2.0 TDI 211 KM", PowerHP = 211, PowerKW = 155, Displacement = 1950, FuelTypeId = die,
                    TorqueNm = 480, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.0m, TopSpeedKmh = 221, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "3.0 TDI S 258 KM", PowerHP = 258, PowerKW = 190, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 580, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.9m, TopSpeedKmh = 254, FuelConsumptionCombined = 6.5m },
            ]);
            AddEngines(GetOrCreateGeneration(macan, "J1 EV (2024–)", "porsche-macan-j1", 2024, null), [
                new EngineVersion { EngineName = "Electric RWD 408 KM", PowerHP = 408, PowerKW = 300, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 520, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "RWD",
                    Cylinders = null, Acceleration0100 = 5.2m, TopSpeedKmh = 220, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Electric Turbo AWD 639 KM", PowerHP = 639, PowerKW = 470, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 1130, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = null, Acceleration0100 = 3.3m, TopSpeedKmh = 260, FuelConsumptionCombined = null },
            ]);
            int taycan = GetOrCreateModel(porscheId, "Taycan", "porsche-taycan");
            AddEngines(GetOrCreateGeneration(taycan, "Y1A (2019–)", "porsche-taycan-y1a", 2019, null), [
                new EngineVersion { EngineName = "4S 571 KM", PowerHP = 571, PowerKW = 420, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 650, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = null, Acceleration0100 = 4.0m, TopSpeedKmh = 250, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "GTS 598 KM", PowerHP = 598, PowerKW = 440, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 850, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = null, Acceleration0100 = 3.7m, TopSpeedKmh = 250, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Turbo 680 KM", PowerHP = 680, PowerKW = 500, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 1050, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = null, Acceleration0100 = 3.2m, TopSpeedKmh = 260, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Turbo S 761 KM", PowerHP = 761, PowerKW = 560, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 1050, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = null, Acceleration0100 = 2.8m, TopSpeedKmh = 260, FuelConsumptionCombined = null },
            ]);
            int panamera = GetOrCreateModel(porscheId, "Panamera", "porsche-panamera");
            AddEngines(GetOrCreateGeneration(panamera, "971 (2016–)", "porsche-panamera-971", 2016, null), [
                new EngineVersion { EngineName = "3.0 T6 330 KM", PowerHP = 330, PowerKW = 243, Displacement = 2995, FuelTypeId = ben,
                    TorqueNm = 450, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.8m, TopSpeedKmh = 272, FuelConsumptionCombined = 9.5m },
                new EngineVersion { EngineName = "3.0 T6 4S 440 KM", PowerHP = 440, PowerKW = 324, Displacement = 2995, FuelTypeId = ben,
                    TorqueNm = 550, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 4.4m, TopSpeedKmh = 296, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "4.0 V8 Turbo S 630 KM", PowerHP = 630, PowerKW = 463, Displacement = 3996, FuelTypeId = ben,
                    TorqueNm = 820, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 3.4m, TopSpeedKmh = 310, FuelConsumptionCombined = 12.5m },
                new EngineVersion { EngineName = "E-Hybrid PHEV 462 KM", PowerHP = 462, PowerKW = 340, Displacement = 2995, FuelTypeId = phev,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 4.6m, TopSpeedKmh = 278, FuelConsumptionCombined = 3.0m },
            ]);

            int m718 = GetOrCreateModel(porscheId, "718 Boxster/Cayman", "porsche-718");
            AddOrReplaceEngines(GetOrFixGeneration(m718, "982 (2016–)", "porsche-718-982", 2016, null), 250, [
                new EngineVersion { EngineName = "2.0 Turbo 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 1988, FuelTypeId = ben,
                    TorqueNm = 380, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 5.1m, TopSpeedKmh = 275, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "2.5 S Turbo 350 KM", PowerHP = 350, PowerKW = 257, Displacement = 2497, FuelTypeId = ben,
                    TorqueNm = 420, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 4.2m, TopSpeedKmh = 285, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "GT4 4.0 420 KM", PowerHP = 420, PowerKW = 309, Displacement = 3995, FuelTypeId = ben,
                    TorqueNm = 420, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 4.4m, TopSpeedKmh = 304, FuelConsumptionCombined = 11.6m },
            ]);

            int m918 = GetOrCreateModel(porscheId, "918 Spyder", "porsche-918-spyder");
            AddOrReplaceEngines(GetOrFixGeneration(m918, "I (2013–2015)", "porsche-918-spyder-2013", 2013, 2015), 800, [
                new EngineVersion { EngineName = "4.6 V8 + electric PHEV 887 KM", PowerHP = 887, PowerKW = 652, Displacement = 4593, FuelTypeId = phev,
                    TorqueNm = 1280, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 2.6m, TopSpeedKmh = 345, FuelConsumptionCombined = 3.1m },
            ]);

            int p356 = GetOrCreateModel(porscheId, "356", "porsche-356");
            AddOrReplaceEngines(GetOrFixGeneration(p356, "I (1948–1965)", "porsche-356-i", 1948, 1965), 60, [
                new EngineVersion { EngineName = "1.6 60 KM", PowerHP = 60, PowerKW = 44, Displacement = 1582, FuelTypeId = ben,
                    TorqueNm = 111, EuroNorm = "Euro 0", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 14.5m, TopSpeedKmh = 160, FuelConsumptionCombined = 9.5m },
                new EngineVersion { EngineName = "1600 Super 90 75 KM", PowerHP = 75, PowerKW = 55, Displacement = 1582, FuelTypeId = ben,
                    TorqueNm = 121, EuroNorm = "Euro 0", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 12.5m, TopSpeedKmh = 175, FuelConsumptionCombined = 9.8m },
            ]);

            int p944 = GetOrCreateModel(porscheId, "924/944", "porsche-924-944");
            AddOrReplaceEngines(GetOrFixGeneration(p944, "I (1976–1991)", "porsche-924-944-i", 1976, 1991), 130, [
                new EngineVersion { EngineName = "924 2.0 125 KM", PowerHP = 125, PowerKW = 92, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 165, EuroNorm = "Euro 0", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 10.0m, TopSpeedKmh = 188, FuelConsumptionCombined = 10.0m },
                new EngineVersion { EngineName = "944 Turbo 2.5 220 KM", PowerHP = 220, PowerKW = 162, Displacement = 2479, FuelTypeId = ben,
                    TorqueNm = 330, EuroNorm = "Euro 0", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 5.9m, TopSpeedKmh = 254, FuelConsumptionCombined = 11.5m },
                new EngineVersion { EngineName = "944 S2 3.0 211 KM", PowerHP = 211, PowerKW = 155, Displacement = 2990, FuelTypeId = ben,
                    TorqueNm = 280, EuroNorm = "Euro 1", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 6.5m, TopSpeedKmh = 240, FuelConsumptionCombined = 11.0m },
            ]);

            // ── LAND ROVER DEFENDER + DISCOVERY + RR SPORT + EVOQUE ──────────────
            int lrId = GetOrCreateBrand("Land Rover", "land-rover", "auta-osobowe");
            int defender = GetOrCreateModel(lrId, "Defender", "lr-defender");
            AddEngines(GetOrCreateGeneration(defender, "L663 (2020–)", "lr-defender-l663", 2020, null), [
                new EngineVersion { EngineName = "P300 2.0T 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 1997, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 191, FuelConsumptionCombined = 10.0m },
                new EngineVersion { EngineName = "P400 3.0T 400 KM", PowerHP = 400, PowerKW = 294, Displacement = 2996, FuelTypeId = ben,
                    TorqueNm = 550, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.8m, TopSpeedKmh = 209, FuelConsumptionCombined = 11.0m },
                new EngineVersion { EngineName = "D200 2.0D 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 430, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 181, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "D300 3.0D 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 2997, FuelTypeId = die,
                    TorqueNm = 650, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 7.7m, TopSpeedKmh = 191, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "P400e PHEV 404 KM", PowerHP = 404, PowerKW = 297, Displacement = 1997, FuelTypeId = phev,
                    TorqueNm = 640, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 5.6m, TopSpeedKmh = 191, FuelConsumptionCombined = 2.5m },
            ]);
            int discovery = GetOrCreateModel(lrId, "Discovery", "lr-discovery");
            AddEngines(GetOrCreateGeneration(discovery, "L462 (2016–)", "lr-discovery-l462", 2016, null), [
                new EngineVersion { EngineName = "P300 2.0T 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 1997, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.1m, TopSpeedKmh = 199, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "D250 3.0D 249 KM", PowerHP = 249, PowerKW = 183, Displacement = 2997, FuelTypeId = die,
                    TorqueNm = 600, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 8.1m, TopSpeedKmh = 195, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "D300 3.0D 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 2997, FuelTypeId = die,
                    TorqueNm = 650, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 7.6m, TopSpeedKmh = 200, FuelConsumptionCombined = 8.2m },
            ]);
            int rrSport = GetOrCreateModel(lrId, "Range Rover Sport", "lr-rr-sport");
            AddEngines(GetOrCreateGeneration(rrSport, "L494 (2013–2022)", "lr-rrs-l494", 2013, 2022), [
                new EngineVersion { EngineName = "P340 3.0T 340 KM", PowerHP = 340, PowerKW = 250, Displacement = 2996, FuelTypeId = ben,
                    TorqueNm = 450, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.5m, TopSpeedKmh = 233, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "SVR 5.0 SC 575 KM", PowerHP = 575, PowerKW = 423, Displacement = 5000, FuelTypeId = ben,
                    TorqueNm = 700, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.5m, TopSpeedKmh = 260, FuelConsumptionCombined = 13.5m },
                new EngineVersion { EngineName = "D300 3.0D 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 2997, FuelTypeId = die,
                    TorqueNm = 650, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 7.2m, TopSpeedKmh = 225, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "P400e PHEV 404 KM", PowerHP = 404, PowerKW = 297, Displacement = 1997, FuelTypeId = phev,
                    TorqueNm = 640, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.3m, TopSpeedKmh = 220, FuelConsumptionCombined = 2.8m },
            ]);
            AddEngines(GetOrCreateGeneration(rrSport, "L461 (2022–)", "lr-rrs-l461", 2022, null), [
                new EngineVersion { EngineName = "P360 3.0T 360 KM", PowerHP = 360, PowerKW = 265, Displacement = 2996, FuelTypeId = ben,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.8m, TopSpeedKmh = 240, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "P530 4.4 V8 530 KM", PowerHP = 530, PowerKW = 390, Displacement = 4395, FuelTypeId = ben,
                    TorqueNm = 750, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.3m, TopSpeedKmh = 265, FuelConsumptionCombined = 13.0m },
                new EngineVersion { EngineName = "D350 3.0D 350 KM", PowerHP = 350, PowerKW = 257, Displacement = 2997, FuelTypeId = die,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.0m, TopSpeedKmh = 242, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "P510e PHEV 510 KM", PowerHP = 510, PowerKW = 375, Displacement = 2997, FuelTypeId = phev,
                    TorqueNm = 750, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.5m, TopSpeedKmh = 242, FuelConsumptionCombined = 2.5m },
            ]);
            int evoque = GetOrCreateModel(lrId, "Range Rover Evoque", "lr-rr-evoque");
            AddEngines(GetOrCreateGeneration(evoque, "L538 (2011–2018)", "lr-evoque-l538", 2011, 2018), [
                new EngineVersion { EngineName = "Si4 2.0T 240 KM", PowerHP = 240, PowerKW = 177, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 340, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.1m, TopSpeedKmh = 217, FuelConsumptionCombined = 9.5m },
                new EngineVersion { EngineName = "TD4 2.0D 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1999, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.5m, TopSpeedKmh = 193, FuelConsumptionCombined = 5.8m },
                new EngineVersion { EngineName = "TD4 2.0D 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 1999, FuelTypeId = die,
                    TorqueNm = 430, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.2m, TopSpeedKmh = 207, FuelConsumptionCombined = 6.0m },
            ]);
            AddEngines(GetOrCreateGeneration(evoque, "L551 (2019–)", "lr-evoque-l551", 2019, null), [
                new EngineVersion { EngineName = "P200 2.0T 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 1997, FuelTypeId = ben,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 209, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "P250 2.0T 249 KM", PowerHP = 249, PowerKW = 183, Displacement = 1997, FuelTypeId = ben,
                    TorqueNm = 365, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.8m, TopSpeedKmh = 221, FuelConsumptionCombined = 9.5m },
                new EngineVersion { EngineName = "D150 2.0D 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1998, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 188, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "D200 2.0D 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 1998, FuelTypeId = die,
                    TorqueNm = 430, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.7m, TopSpeedKmh = 199, FuelConsumptionCombined = 5.8m },
                new EngineVersion { EngineName = "P300e PHEV 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 1497, FuelTypeId = phev,
                    TorqueNm = 540, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 3, Acceleration0100 = 6.4m, TopSpeedKmh = 209, FuelConsumptionCombined = 1.8m },
            ]);

            int rangeRover = GetOrCreateModel(lrId, "Range Rover", "lr-range-rover");
            PrepareGenerations(rangeRover,
                ("L405 (2012–2022)", "lr-range-rover-l405", 2012, 2022),
                ("L460 (2022–)", "lr-range-rover-l460", 2022, null));
            AddOrReplaceEngines(GetOrFixGeneration(rangeRover, "L405 (2012–2022)", "lr-range-rover-l405", 2012, 2022), 250, [
                new EngineVersion { EngineName = "P400 3.0T 400 KM", PowerHP = 400, PowerKW = 294, Displacement = 2996, FuelTypeId = ben,
                    TorqueNm = 550, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.4m, TopSpeedKmh = 225, FuelConsumptionCombined = 10.9m },
                new EngineVersion { EngineName = "SVAutobiography 5.0 SC 565 KM", PowerHP = 565, PowerKW = 416, Displacement = 5000, FuelTypeId = ben,
                    TorqueNm = 700, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 5.4m, TopSpeedKmh = 250, FuelConsumptionCombined = 14.5m },
                new EngineVersion { EngineName = "D300 3.0D 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 2997, FuelTypeId = die,
                    TorqueNm = 650, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.5m, TopSpeedKmh = 225, FuelConsumptionCombined = 7.8m },
                new EngineVersion { EngineName = "P400e PHEV 404 KM", PowerHP = 404, PowerKW = 297, Displacement = 1997, FuelTypeId = phev,
                    TorqueNm = 640, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.8m, TopSpeedKmh = 220, FuelConsumptionCombined = 2.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(rangeRover, "L460 (2022–)", "lr-range-rover-l460", 2022, null), 300, [
                new EngineVersion { EngineName = "P400 3.0T 400 KM", PowerHP = 400, PowerKW = 294, Displacement = 2996, FuelTypeId = mild,
                    TorqueNm = 550, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.1m, TopSpeedKmh = 225, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "P530 4.4 V8 530 KM", PowerHP = 530, PowerKW = 390, Displacement = 4395, FuelTypeId = ben,
                    TorqueNm = 750, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.6m, TopSpeedKmh = 250, FuelConsumptionCombined = 13.0m },
                new EngineVersion { EngineName = "D350 3.0D 350 KM", PowerHP = 350, PowerKW = 257, Displacement = 2997, FuelTypeId = die,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.8m, TopSpeedKmh = 225, FuelConsumptionCombined = 8.2m },
                new EngineVersion { EngineName = "P510e PHEV 510 KM", PowerHP = 510, PowerKW = 375, Displacement = 2997, FuelTypeId = phev,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.2m, TopSpeedKmh = 220, FuelConsumptionCombined = 2.5m },
            ]);

            int velar = GetOrCreateModel(lrId, "Range Rover Velar", "lr-rr-velar");
            AddOrReplaceEngines(GetOrFixGeneration(velar, "L560 (2017–)", "lr-velar-l560", 2017, null), 200, [
                new EngineVersion { EngineName = "P250 2.0T 250 KM", PowerHP = 250, PowerKW = 184, Displacement = 1997, FuelTypeId = ben,
                    TorqueNm = 365, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 217, FuelConsumptionCombined = 9.5m },
                new EngineVersion { EngineName = "P400 3.0T 400 KM", PowerHP = 400, PowerKW = 294, Displacement = 2996, FuelTypeId = mild,
                    TorqueNm = 550, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.7m, TopSpeedKmh = 250, FuelConsumptionCombined = 10.9m },
                new EngineVersion { EngineName = "D200 2.0D 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 430, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.4m, TopSpeedKmh = 209, FuelConsumptionCombined = 6.5m },
            ]);

            int discoverySport = GetOrCreateModel(lrId, "Discovery Sport", "lr-discovery-sport");
            AddOrReplaceEngines(GetOrFixGeneration(discoverySport, "L550 (2014–)", "lr-discovery-sport-l550", 2014, null), 130, [
                new EngineVersion { EngineName = "P200 2.0T 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 1997, FuelTypeId = ben,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.2m, TopSpeedKmh = 201, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "D165 2.0D 165 KM", PowerHP = 165, PowerKW = 121, Displacement = 1999, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 10.0m, TopSpeedKmh = 188, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "P300e PHEV 309 KM", PowerHP = 309, PowerKW = 227, Displacement = 1497, FuelTypeId = phev,
                    TorqueNm = 540, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 3, Acceleration0100 = 6.6m, TopSpeedKmh = 209, FuelConsumptionCombined = 1.9m },
            ]);

            int freelander = GetOrCreateModel(lrId, "Freelander", "lr-freelander");
            AddOrReplaceEngines(GetOrFixGeneration(freelander, "II (2006–2014)", "lr-freelander-ii", 2006, 2014), 100, [
                new EngineVersion { EngineName = "2.2 TD4 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 2179, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 10.9m, TopSpeedKmh = 180, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "2.0 Si4 240 KM", PowerHP = 240, PowerKW = 177, Displacement = 1999, FuelTypeId = ben,
                    TorqueNm = 340, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.7m, TopSpeedKmh = 200, FuelConsumptionCombined = 9.5m },
            ]);

            // ── JAGUAR XE + XF + F-PACE + I-PACE + F-TYPE ────────────────────────
            int jagId = GetOrCreateBrand("Jaguar", "jaguar", "auta-osobowe");
            int xe = GetOrCreateModel(jagId, "XE", "jaguar-xe");
            AddEngines(GetOrCreateGeneration(xe, "X760 (2015–)", "jaguar-xe-x760", 2015, null), [
                new EngineVersion { EngineName = "P200 2.0T 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 1997, FuelTypeId = ben,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.3m, TopSpeedKmh = 237, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "P250 2.0T 250 KM", PowerHP = 250, PowerKW = 184, Displacement = 1997, FuelTypeId = ben,
                    TorqueNm = 365, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.5m, TopSpeedKmh = 250, FuelConsumptionCombined = 9.0m },
                new EngineVersion { EngineName = "D150 2.0D 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1998, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 215, FuelConsumptionCombined = 5.0m },
                new EngineVersion { EngineName = "D180 2.0D 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 1998, FuelTypeId = die,
                    TorqueNm = 430, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.8m, TopSpeedKmh = 222, FuelConsumptionCombined = 5.2m },
            ]);
            int xf = GetOrCreateModel(jagId, "XF", "jaguar-xf");
            AddEngines(GetOrCreateGeneration(xf, "X260 (2015–)", "jaguar-xf-x260", 2015, null), [
                new EngineVersion { EngineName = "P250 2.0T 250 KM", PowerHP = 250, PowerKW = 184, Displacement = 1997, FuelTypeId = ben,
                    TorqueNm = 365, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 6.8m, TopSpeedKmh = 250, FuelConsumptionCombined = 9.0m },
                new EngineVersion { EngineName = "P300 2.0T 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 1997, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 5.9m, TopSpeedKmh = 260, FuelConsumptionCombined = 9.5m },
                new EngineVersion { EngineName = "D165 2.0D 165 KM", PowerHP = 165, PowerKW = 121, Displacement = 1998, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 8.8m, TopSpeedKmh = 225, FuelConsumptionCombined = 5.0m },
                new EngineVersion { EngineName = "D204 2.0D 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 1998, FuelTypeId = die,
                    TorqueNm = 430, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 235, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "D300 3.0D 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 2993, FuelTypeId = die,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.9m, TopSpeedKmh = 250, FuelConsumptionCombined = 6.5m },
            ]);
            int fpace = GetOrCreateModel(jagId, "F-Pace", "jaguar-f-pace");
            AddEngines(GetOrCreateGeneration(fpace, "X761 (2016–)", "jaguar-fpace-x761", 2016, null), [
                new EngineVersion { EngineName = "P250 2.0T 250 KM", PowerHP = 250, PowerKW = 184, Displacement = 1997, FuelTypeId = ben,
                    TorqueNm = 365, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.8m, TopSpeedKmh = 240, FuelConsumptionCombined = 9.5m },
                new EngineVersion { EngineName = "P400 3.0T 400 KM", PowerHP = 400, PowerKW = 294, Displacement = 2996, FuelTypeId = ben,
                    TorqueNm = 550, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.0m, TopSpeedKmh = 265, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "D165 2.0D 165 KM", PowerHP = 165, PowerKW = 121, Displacement = 1998, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.7m, TopSpeedKmh = 215, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "D204 2.0D 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 1998, FuelTypeId = die,
                    TorqueNm = 430, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.6m, TopSpeedKmh = 225, FuelConsumptionCombined = 5.8m },
                new EngineVersion { EngineName = "P400e PHEV 404 KM", PowerHP = 404, PowerKW = 297, Displacement = 1997, FuelTypeId = phev,
                    TorqueNm = 640, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 5.1m, TopSpeedKmh = 234, FuelConsumptionCombined = 2.0m },
            ]);
            int ipace = GetOrCreateModel(jagId, "I-Pace", "jaguar-i-pace");
            AddEngines(GetOrCreateGeneration(ipace, "X590 EV (2018–)", "jaguar-ipace-x590", 2018, null), [
                new EngineVersion { EngineName = "EV400 AWD 400 KM", PowerHP = 400, PowerKW = 294, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 696, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = null, Acceleration0100 = 4.8m, TopSpeedKmh = 200, FuelConsumptionCombined = null },
            ]);
            int ftype = GetOrCreateModel(jagId, "F-Type", "jaguar-f-type");
            AddEngines(GetOrCreateGeneration(ftype, "X152 (2012–)", "jaguar-ftype-x152", 2012, null), [
                new EngineVersion { EngineName = "2.0T 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 1997, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 5.7m, TopSpeedKmh = 250, FuelConsumptionCombined = 9.5m },
                new EngineVersion { EngineName = "3.0 V6 340 KM", PowerHP = 340, PowerKW = 250, Displacement = 2995, FuelTypeId = ben,
                    TorqueNm = 450, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 5.1m, TopSpeedKmh = 260, FuelConsumptionCombined = 11.0m },
                new EngineVersion { EngineName = "5.0 V8 R 450 KM", PowerHP = 450, PowerKW = 331, Displacement = 5000, FuelTypeId = ben,
                    TorqueNm = 580, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.2m, TopSpeedKmh = 280, FuelConsumptionCombined = 13.0m },
                new EngineVersion { EngineName = "SVR 5.0 V8 575 KM", PowerHP = 575, PowerKW = 423, Displacement = 5000, FuelTypeId = ben,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 3.7m, TopSpeedKmh = 300, FuelConsumptionCombined = 14.0m },
            ]);

            // ── GENESIS G70/G80/GV70/GV80 ────────────────────────────────────────
            int genId = GetOrCreateBrand("Genesis", "genesis", "auta-osobowe");
            int g70 = GetOrCreateModel(genId, "G70", "genesis-g70");
            AddEngines(GetOrCreateGeneration(g70, "I (2017–)", "genesis-g70-i", 2017, null), [
                new EngineVersion { EngineName = "2.0T 252 KM", PowerHP = 252, PowerKW = 185, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 353, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 6.0m, TopSpeedKmh = 240, FuelConsumptionCombined = 9.5m },
                new EngineVersion { EngineName = "3.3T Sport 370 KM", PowerHP = 370, PowerKW = 272, Displacement = 3342, FuelTypeId = ben,
                    TorqueNm = 510, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 4.7m, TopSpeedKmh = 270, FuelConsumptionCombined = 11.5m },
                new EngineVersion { EngineName = "2.2 CRDi 202 KM", PowerHP = 202, PowerKW = 149, Displacement = 2199, FuelTypeId = die,
                    TorqueNm = 440, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.2m, TopSpeedKmh = 240, FuelConsumptionCombined = 6.0m },
            ]);
            int g80 = GetOrCreateModel(genId, "G80", "genesis-g80");
            AddEngines(GetOrCreateGeneration(g80, "III (2020–)", "genesis-g80-iii", 2020, null), [
                new EngineVersion { EngineName = "2.5T 304 KM", PowerHP = 304, PowerKW = 224, Displacement = 2497, FuelTypeId = ben,
                    TorqueNm = 422, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 5.8m, TopSpeedKmh = 235, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "3.5T V6 380 KM", PowerHP = 380, PowerKW = 279, Displacement = 3470, FuelTypeId = ben,
                    TorqueNm = 530, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 4.9m, TopSpeedKmh = 250, FuelConsumptionCombined = 12.0m },
                new EngineVersion { EngineName = "Electrified EV 365 KM", PowerHP = 365, PowerKW = 268, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = null, Acceleration0100 = 4.9m, TopSpeedKmh = 225, FuelConsumptionCombined = null },
            ]);
            int gv70 = GetOrCreateModel(genId, "GV70", "genesis-gv70");
            AddEngines(GetOrCreateGeneration(gv70, "I (2021–)", "genesis-gv70-i", 2021, null), [
                new EngineVersion { EngineName = "2.5T 304 KM", PowerHP = 304, PowerKW = 224, Displacement = 2497, FuelTypeId = ben,
                    TorqueNm = 422, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 5.8m, TopSpeedKmh = 235, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "3.5T V6 380 KM", PowerHP = 380, PowerKW = 279, Displacement = 3470, FuelTypeId = ben,
                    TorqueNm = 530, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 4.5m, TopSpeedKmh = 240, FuelConsumptionCombined = 11.5m },
                new EngineVersion { EngineName = "Electrified EV 490 KM", PowerHP = 490, PowerKW = 360, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = null, Acceleration0100 = 4.2m, TopSpeedKmh = 235, FuelConsumptionCombined = null },
            ]);
            int gv80 = GetOrCreateModel(genId, "GV80", "genesis-gv80");
            AddEngines(GetOrCreateGeneration(gv80, "I (2020–)", "genesis-gv80-i", 2020, null), [
                new EngineVersion { EngineName = "2.5T 304 KM", PowerHP = 304, PowerKW = 224, Displacement = 2497, FuelTypeId = ben,
                    TorqueNm = 422, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.0m, TopSpeedKmh = 220, FuelConsumptionCombined = 11.0m },
                new EngineVersion { EngineName = "3.5T V6 380 KM", PowerHP = 380, PowerKW = 279, Displacement = 3470, FuelTypeId = ben,
                    TorqueNm = 530, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.5m, TopSpeedKmh = 240, FuelConsumptionCombined = 12.5m },
                new EngineVersion { EngineName = "3.0D 278 KM", PowerHP = 278, PowerKW = 204, Displacement = 2999, FuelTypeId = die,
                    TorqueNm = 600, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.0m, TopSpeedKmh = 225, FuelConsumptionCombined = 8.0m },
            ]);

            // ── MG4 + ZS EV + MG5 + MG3 ─────────────────────────────────────────
            int mgId = GetOrCreateBrand("MG", "mg", "auta-osobowe");
            int mg4 = GetOrCreateModel(mgId, "MG4", "mg-4");
            AddEngines(GetOrCreateGeneration(mg4, "MG4 EV (2022–)", "mg4-ev-2022", 2022, null), [
                new EngineVersion { EngineName = "51 kWh RWD 170 KM", PowerHP = 170, PowerKW = 125, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "RWD",
                    Cylinders = null, Acceleration0100 = 7.7m, TopSpeedKmh = 160, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "64 kWh RWD 204 KM", PowerHP = 204, PowerKW = 150, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "RWD",
                    Cylinders = null, Acceleration0100 = 7.7m, TopSpeedKmh = 160, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "77 kWh AWD 435 KM", PowerHP = 435, PowerKW = 320, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 600, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = null, Acceleration0100 = 3.8m, TopSpeedKmh = 200, FuelConsumptionCombined = null },
            ]);
            int zsEv = GetOrCreateModel(mgId, "ZS EV", "mg-zs-ev");
            AddEngines(GetOrCreateGeneration(zsEv, "I (2019–)", "mg-zs-ev-i", 2019, null), [
                new EngineVersion { EngineName = "44.5 kWh 143 KM", PowerHP = 143, PowerKW = 105, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 280, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = null, Acceleration0100 = 8.2m, TopSpeedKmh = 175, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "72.6 kWh 156 KM", PowerHP = 156, PowerKW = 115, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 280, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = null, Acceleration0100 = 8.6m, TopSpeedKmh = 175, FuelConsumptionCombined = null },
            ]);
            int mg5 = GetOrCreateModel(mgId, "MG5", "mg-5");
            AddEngines(GetOrCreateGeneration(mg5, "I (2020–)", "mg5-ev-i", 2020, null), [
                new EngineVersion { EngineName = "50.3 kWh 156 KM", PowerHP = 156, PowerKW = 115, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 280, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = null, Acceleration0100 = 7.7m, TopSpeedKmh = 185, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "61 kWh 156 KM", PowerHP = 156, PowerKW = 115, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 280, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = null, Acceleration0100 = 7.7m, TopSpeedKmh = 185, FuelConsumptionCombined = null },
            ]);
            int mg3 = GetOrCreateModel(mgId, "MG3", "mg-3");
            AddEngines(GetOrCreateGeneration(mg3, "III Hybrid+ (2023–)", "mg3-hybrid-iii", 2023, null), [
                new EngineVersion { EngineName = "1.5 Hybrid 194 KM", PowerHP = 194, PowerKW = 143, Displacement = 1490, FuelTypeId = hyb,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.9m, TopSpeedKmh = 185, FuelConsumptionCombined = 4.5m },
            ]);

            // ── BYD ATTO 3 + HAN + DOLPHIN + SEAL ────────────────────────────────
            int bydId = GetOrCreateBrand("BYD", "byd", "auta-osobowe");
            int atto3 = GetOrCreateModel(bydId, "Atto 3", "byd-atto-3");
            AddEngines(GetOrCreateGeneration(atto3, "I (2022–)", "byd-atto3-i", 2022, null), [
                new EngineVersion { EngineName = "60.5 kWh 204 KM", PowerHP = 204, PowerKW = 150, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 310, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = null, Acceleration0100 = 7.3m, TopSpeedKmh = 160, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "60.5 kWh AWD 313 KM", PowerHP = 313, PowerKW = 230, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 430, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = null, Acceleration0100 = 6.9m, TopSpeedKmh = 160, FuelConsumptionCombined = null },
            ]);
            int han = GetOrCreateModel(bydId, "Han", "byd-han");
            AddEngines(GetOrCreateGeneration(han, "I (2020–)", "byd-han-i", 2020, null), [
                new EngineVersion { EngineName = "85.4 kWh RWD 272 KM", PowerHP = 272, PowerKW = 200, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "RWD",
                    Cylinders = null, Acceleration0100 = 7.9m, TopSpeedKmh = 180, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "85.4 kWh AWD 517 KM", PowerHP = 517, PowerKW = 380, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = null, Acceleration0100 = 3.9m, TopSpeedKmh = 180, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "DM-i PHEV 245 KM", PowerHP = 245, PowerKW = 180, Displacement = 1498, FuelTypeId = phev,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.4m, TopSpeedKmh = 185, FuelConsumptionCombined = 1.5m },
            ]);
            int dolphin = GetOrCreateModel(bydId, "Dolphin", "byd-dolphin");
            AddEngines(GetOrCreateGeneration(dolphin, "I (2021–)", "byd-dolphin-i", 2021, null), [
                new EngineVersion { EngineName = "44.9 kWh 70 KM", PowerHP = 70, PowerKW = 51, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 180, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = null, Acceleration0100 = 12.3m, TopSpeedKmh = 130, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "60.4 kWh 204 KM", PowerHP = 204, PowerKW = 150, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 310, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = null, Acceleration0100 = 7.0m, TopSpeedKmh = 160, FuelConsumptionCombined = null },
            ]);
            int seal = GetOrCreateModel(bydId, "Seal", "byd-seal");
            AddEngines(GetOrCreateGeneration(seal, "I (2022–)", "byd-seal-i", 2022, null), [
                new EngineVersion { EngineName = "82.5 kWh RWD 313 KM", PowerHP = 313, PowerKW = 230, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 360, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "RWD",
                    Cylinders = null, Acceleration0100 = 5.9m, TopSpeedKmh = 180, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "82.5 kWh AWD 530 KM", PowerHP = 530, PowerKW = 390, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 670, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = null, Acceleration0100 = 3.8m, TopSpeedKmh = 180, FuelConsumptionCombined = null },
            ]);

            // ── LAMBORGHINI AVENTADOR + HURACÁN + URUS + REVUELTO ─────────────────
            int lambId = GetOrCreateBrand("Lamborghini", "lamborghini", "auta-osobowe");
            int aventador = GetOrCreateModel(lambId, "Aventador", "lamborghini-aventador");
            ForceReplaceEngines(GetOrFixGeneration(aventador, "Aventador (2011–2022)", "lamborghini-aventador-2011", 2011, 2022), [
                new EngineVersion { EngineName = "6.5 V12 LP700-4 700 KM", PowerHP = 700, PowerKW = 515, Displacement = 6498, FuelTypeId = ben,
                    TorqueNm = 690, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 2.9m, TopSpeedKmh = 350, FuelConsumptionCombined = 19.8m },
                new EngineVersion { EngineName = "6.5 V12 LP750-4 SV 750 KM", PowerHP = 750, PowerKW = 552, Displacement = 6498, FuelTypeId = ben,
                    TorqueNm = 690, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 2.8m, TopSpeedKmh = 350, FuelConsumptionCombined = 19.8m },
                new EngineVersion { EngineName = "6.5 V12 S 740 KM", PowerHP = 740, PowerKW = 544, Displacement = 6498, FuelTypeId = ben,
                    TorqueNm = 690, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 2.9m, TopSpeedKmh = 350, FuelConsumptionCombined = 19.8m },
                new EngineVersion { EngineName = "6.5 V12 SVJ 770 KM", PowerHP = 770, PowerKW = 566, Displacement = 6498, FuelTypeId = ben,
                    TorqueNm = 720, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 2.8m, TopSpeedKmh = 350, FuelConsumptionCombined = 19.8m },
                new EngineVersion { EngineName = "6.5 V12 Ultimae 780 KM", PowerHP = 780, PowerKW = 574, Displacement = 6498, FuelTypeId = ben,
                    TorqueNm = 720, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 2.8m, TopSpeedKmh = 355, FuelConsumptionCombined = 19.8m },
            ]);
            int huracan = GetOrCreateModel(lambId, "Huracán", "lamborghini-huracan");
            AddEngines(GetOrCreateGeneration(huracan, "LP610-4 (2014–2021)", "lamborghini-huracan-lp610", 2014, 2021), [
                new EngineVersion { EngineName = "5.2 V10 610 KM", PowerHP = 610, PowerKW = 449, Displacement = 5204, FuelTypeId = ben,
                    TorqueNm = 560, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 10, Acceleration0100 = 3.2m, TopSpeedKmh = 325, FuelConsumptionCombined = 13.5m },
            ]);
            AddEngines(GetOrCreateGeneration(huracan, "EVO (2019–2024)", "lamborghini-huracan-evo", 2019, 2024), [
                new EngineVersion { EngineName = "5.2 V10 640 KM", PowerHP = 640, PowerKW = 471, Displacement = 5204, FuelTypeId = ben,
                    TorqueNm = 600, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 10, Acceleration0100 = 2.9m, TopSpeedKmh = 325, FuelConsumptionCombined = 14.0m },
            ]);
            int urus = GetOrCreateModel(lambId, "Urus", "lamborghini-urus");
            AddEngines(GetOrCreateGeneration(urus, "Urus (2018–)", "lamborghini-urus-2018", 2018, null), [
                new EngineVersion { EngineName = "4.0 V8 Twin-Turbo 650 KM", PowerHP = 650, PowerKW = 478, Displacement = 3996, FuelTypeId = ben,
                    TorqueNm = 850, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 3.6m, TopSpeedKmh = 305, FuelConsumptionCombined = 13.5m },
                new EngineVersion { EngineName = "S/Performante 4.0 V8 666 KM", PowerHP = 666, PowerKW = 490, Displacement = 3996, FuelTypeId = ben,
                    TorqueNm = 850, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 3.3m, TopSpeedKmh = 306, FuelConsumptionCombined = 14.0m },
            ]);
            int revuelto = GetOrCreateModel(lambId, "Revuelto", "lamborghini-revuelto");
            AddEngines(GetOrCreateGeneration(revuelto, "LB744 (2023–)", "lamborghini-revuelto-2023", 2023, null), [
                new EngineVersion { EngineName = "6.5 V12 + electric PHEV 1001 KM", PowerHP = 1001, PowerKW = 736, Displacement = 6498, FuelTypeId = phev,
                    TorqueNm = 1000, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 2.5m, TopSpeedKmh = 350, FuelConsumptionCombined = 8.0m },
            ]);

            int countach = GetOrCreateModel(lambId, "Countach", "lamborghini-countach");
            AddOrReplaceEngines(GetOrFixGeneration(countach, "LP5000 QV (1985–1990)", "lamborghini-countach-lp5000qv", 1985, 1990), 380, [
                new EngineVersion { EngineName = "5.2 V12 455 KM", PowerHP = 455, PowerKW = 335, Displacement = 5167, FuelTypeId = ben,
                    TorqueNm = 500, EuroNorm = "Euro 0", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 4.7m, TopSpeedKmh = 295, FuelConsumptionCombined = 25.0m },
            ]);

            int diablo = GetOrCreateModel(lambId, "Diablo", "lamborghini-diablo");
            PrepareGenerations(diablo,
                ("I (1990–1998)", "lamborghini-diablo-i", 1990, 1998),
                ("VT/SV (1998–2001)", "lamborghini-diablo-vt-sv", 1998, 2001));
            AddOrReplaceEngines(GetOrFixGeneration(diablo, "I (1990–1998)", "lamborghini-diablo-i", 1990, 1998), 480, [
                new EngineVersion { EngineName = "5.7 V12 492 KM", PowerHP = 492, PowerKW = 362, Displacement = 5707, FuelTypeId = ben,
                    TorqueNm = 580, EuroNorm = "Euro 0", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 4.5m, TopSpeedKmh = 325, FuelConsumptionCombined = 26.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(diablo, "VT/SV (1998–2001)", "lamborghini-diablo-vt-sv", 1998, 2001), 500, [
                new EngineVersion { EngineName = "6.0 V12 SV 550 KM", PowerHP = 550, PowerKW = 404, Displacement = 5992, FuelTypeId = ben,
                    TorqueNm = 620, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 4.0m, TopSpeedKmh = 337, FuelConsumptionCombined = 27.0m },
            ]);

            int espada = GetOrCreateModel(lambId, "Espada", "lamborghini-espada");
            AddOrReplaceEngines(GetOrFixGeneration(espada, "Series III (1972–1978)", "lamborghini-espada-series3", 1972, 1978), 340, [
                new EngineVersion { EngineName = "4.0 V12 350 KM", PowerHP = 350, PowerKW = 257, Displacement = 3929, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 0", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 6.5m, TopSpeedKmh = 245, FuelConsumptionCombined = 22.0m },
            ]);

            int gallardo = GetOrCreateModel(lambId, "Gallardo", "lamborghini-gallardo");
            PrepareGenerations(gallardo,
                ("I (2003–2008)", "lamborghini-gallardo-i", 2003, 2008),
                ("LP560 (2008–2013)", "lamborghini-gallardo-lp560", 2008, 2013));
            AddOrReplaceEngines(GetOrFixGeneration(gallardo, "I (2003–2008)", "lamborghini-gallardo-i", 2003, 2008), 480, [
                new EngineVersion { EngineName = "5.0 V10 500 KM", PowerHP = 500, PowerKW = 368, Displacement = 4961, FuelTypeId = ben,
                    TorqueNm = 510, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 10, Acceleration0100 = 4.2m, TopSpeedKmh = 309, FuelConsumptionCombined = 19.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(gallardo, "LP560 (2008–2013)", "lamborghini-gallardo-lp560", 2008, 2013), 550, [
                new EngineVersion { EngineName = "5.2 V10 LP560-4 560 KM", PowerHP = 560, PowerKW = 412, Displacement = 5204, FuelTypeId = ben,
                    TorqueNm = 540, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 10, Acceleration0100 = 3.7m, TopSpeedKmh = 325, FuelConsumptionCombined = 18.9m },
                new EngineVersion { EngineName = "5.2 V10 LP570-4 Superleggera 570 KM", PowerHP = 570, PowerKW = 419, Displacement = 5204, FuelTypeId = ben,
                    TorqueNm = 540, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 10, Acceleration0100 = 3.4m, TopSpeedKmh = 325, FuelConsumptionCombined = 18.9m },
            ]);

            int lm002 = GetOrCreateModel(lambId, "LM002", "lamborghini-lm002");
            AddOrReplaceEngines(GetOrFixGeneration(lm002, "I (1986–1993)", "lamborghini-lm002-i", 1986, 1993), 400, [
                new EngineVersion { EngineName = "5.2 V12 450 KM", PowerHP = 450, PowerKW = 331, Displacement = 5167, FuelTypeId = ben,
                    TorqueNm = 500, EuroNorm = "Euro 0", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 7.7m, TopSpeedKmh = 210, FuelConsumptionCombined = 35.0m },
            ]);

            int murcielago = GetOrCreateModel(lambId, "Murciélago", "lamborghini-murcielago");
            PrepareGenerations(murcielago,
                ("I (2001–2006)", "lamborghini-murcielago-i", 2001, 2006),
                ("LP640 (2006–2010)", "lamborghini-murcielago-lp640", 2006, 2010));
            AddOrReplaceEngines(GetOrFixGeneration(murcielago, "I (2001–2006)", "lamborghini-murcielago-i", 2001, 2006), 550, [
                new EngineVersion { EngineName = "6.2 V12 580 KM", PowerHP = 580, PowerKW = 426, Displacement = 6192, FuelTypeId = ben,
                    TorqueNm = 650, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 3.8m, TopSpeedKmh = 330, FuelConsumptionCombined = 21.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(murcielago, "LP640 (2006–2010)", "lamborghini-murcielago-lp640", 2006, 2010), 620, [
                new EngineVersion { EngineName = "6.5 V12 LP640 640 KM", PowerHP = 640, PowerKW = 471, Displacement = 6496, FuelTypeId = ben,
                    TorqueNm = 660, EuroNorm = "Euro 4", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 3.4m, TopSpeedKmh = 340, FuelConsumptionCombined = 21.6m },
                new EngineVersion { EngineName = "6.5 V12 LP670-4 SV 670 KM", PowerHP = 670, PowerKW = 493, Displacement = 6496, FuelTypeId = ben,
                    TorqueNm = 660, EuroNorm = "Euro 4", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 3.2m, TopSpeedKmh = 342, FuelConsumptionCombined = 22.0m },
            ]);
        }

        // ── Ferrari ──────────────────────────────────────────────────────────────
        {
            int ferrId = GetOrCreateBrand("Ferrari", "ferrari", "osobowe");
            int f488 = GetOrCreateModel(ferrId, "488", "ferrari-488");
            AddEngines(GetOrCreateGeneration(f488, "488 GTB/Spider (2015–2019)", "ferrari-488-gtb", 2015, 2019), [
                new EngineVersion { EngineName = "3.9 V8 Twin-Turbo 670 KM", PowerHP = 670, PowerKW = 493, Displacement = 3902, FuelTypeId = ben,
                    TorqueNm = 760, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.0m, TopSpeedKmh = 330, FuelConsumptionCombined = 11.4m },
            ]);
            AddEngines(GetOrCreateGeneration(f488, "488 Pista (2018–2019)", "ferrari-488-pista", 2018, 2019), [
                new EngineVersion { EngineName = "3.9 V8 Twin-Turbo 720 KM", PowerHP = 720, PowerKW = 530, Displacement = 3902, FuelTypeId = ben,
                    TorqueNm = 770, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 2.85m, TopSpeedKmh = 340, FuelConsumptionCombined = 12.0m },
            ]);
            int f8 = GetOrCreateModel(ferrId, "F8", "ferrari-f8");
            AddEngines(GetOrCreateGeneration(f8, "F8 Tributo/Spider (2019–2022)", "ferrari-f8-tributo", 2019, 2022), [
                new EngineVersion { EngineName = "3.9 V8 Twin-Turbo 720 KM", PowerHP = 720, PowerKW = 530, Displacement = 3902, FuelTypeId = ben,
                    TorqueNm = 770, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 2.9m, TopSpeedKmh = 340, FuelConsumptionCombined = 11.6m },
            ]);
            int roma = GetOrCreateModel(ferrId, "Roma", "ferrari-roma");
            AddEngines(GetOrCreateGeneration(roma, "Roma/Spider (2019–)", "ferrari-roma-2019", 2019, null), [
                new EngineVersion { EngineName = "3.9 V8 Twin-Turbo 620 KM", PowerHP = 620, PowerKW = 456, Displacement = 3855, FuelTypeId = ben,
                    TorqueNm = 760, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.4m, TopSpeedKmh = 320, FuelConsumptionCombined = 10.7m },
            ]);
            int sf90 = GetOrCreateModel(ferrId, "SF90 Stradale", "ferrari-sf90");
            AddEngines(GetOrCreateGeneration(sf90, "SF90 Stradale/Spider (2019–)", "ferrari-sf90-2019", 2019, null), [
                new EngineVersion { EngineName = "4.0 V8 + 3 el. PHEV 1000 KM", PowerHP = 1000, PowerKW = 735, Displacement = 3990, FuelTypeId = phev,
                    TorqueNm = 800, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 2.5m, TopSpeedKmh = 340, FuelConsumptionCombined = 8.0m },
            ]);
            int puro = GetOrCreateModel(ferrId, "Purosangue", "ferrari-purosangue");
            AddEngines(GetOrCreateGeneration(puro, "F176 (2022–)", "ferrari-purosangue-2022", 2022, null), [
                new EngineVersion { EngineName = "6.5 V12 725 KM", PowerHP = 725, PowerKW = 533, Displacement = 6496, FuelTypeId = ben,
                    TorqueNm = 716, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 3.3m, TopSpeedKmh = 310, FuelConsumptionCombined = 14.9m },
            ]);

            int f296 = GetOrCreateModel(ferrId, "296 GTB", "ferrari-296-gtb");
            AddOrReplaceEngines(GetOrFixGeneration(f296, "I (2021–)", "ferrari-296-gtb-i", 2021, null), 700, [
                new EngineVersion { EngineName = "3.0 V6 Twin-Turbo Hybrid 830 KM", PowerHP = 830, PowerKW = 610, Displacement = 2992, FuelTypeId = phev,
                    TorqueNm = 740, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 2.9m, TopSpeedKmh = 330, FuelConsumptionCombined = 5.8m },
            ]);

            int f308 = GetOrCreateModel(ferrId, "308", "ferrari-308");
            AddOrReplaceEngines(GetOrFixGeneration(f308, "I (1975–1985)", "ferrari-308-i", 1975, 1985), 200, [
                new EngineVersion { EngineName = "3.0 V8 255 KM", PowerHP = 255, PowerKW = 188, Displacement = 2927, FuelTypeId = ben,
                    TorqueNm = 245, EuroNorm = "Euro 0", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 6.6m, TopSpeedKmh = 245, FuelConsumptionCombined = 15.5m },
            ]);

            int f328 = GetOrCreateModel(ferrId, "328", "ferrari-328");
            AddOrReplaceEngines(GetOrFixGeneration(f328, "I (1985–1989)", "ferrari-328-i", 1985, 1989), 250, [
                new EngineVersion { EngineName = "3.2 V8 270 KM", PowerHP = 270, PowerKW = 199, Displacement = 3185, FuelTypeId = ben,
                    TorqueNm = 304, EuroNorm = "Euro 0", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 5.6m, TopSpeedKmh = 260, FuelConsumptionCombined = 15.8m },
            ]);

            int f348 = GetOrCreateModel(ferrId, "348", "ferrari-348");
            AddOrReplaceEngines(GetOrFixGeneration(f348, "I (1989–1995)", "ferrari-348-i", 1989, 1995), 280, [
                new EngineVersion { EngineName = "3.4 V8 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 3405, FuelTypeId = ben,
                    TorqueNm = 324, EuroNorm = "Euro 1", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 5.6m, TopSpeedKmh = 275, FuelConsumptionCombined = 16.2m },
            ]);

            int f355 = GetOrCreateModel(ferrId, "355", "ferrari-355");
            AddOrReplaceEngines(GetOrFixGeneration(f355, "I (1994–1999)", "ferrari-355-i", 1994, 1999), 350, [
                new EngineVersion { EngineName = "3.5 V8 380 KM", PowerHP = 380, PowerKW = 280, Displacement = 3496, FuelTypeId = ben,
                    TorqueNm = 363, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 4.7m, TopSpeedKmh = 295, FuelConsumptionCombined = 17.0m },
            ]);

            int f430 = GetOrCreateModel(ferrId, "430", "ferrari-430");
            AddOrReplaceEngines(GetOrFixGeneration(f430, "I (2004–2009)", "ferrari-430-i", 2004, 2009), 400, [
                new EngineVersion { EngineName = "4.3 V8 490 KM", PowerHP = 490, PowerKW = 360, Displacement = 4308, FuelTypeId = ben,
                    TorqueNm = 465, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 4.0m, TopSpeedKmh = 315, FuelConsumptionCombined = 18.7m },
            ]);

            int f812 = GetOrCreateModel(ferrId, "812 Superfast", "ferrari-812-superfast");
            AddOrReplaceEngines(GetOrFixGeneration(f812, "I (2017–2022)", "ferrari-812-superfast-i", 2017, 2022), 700, [
                new EngineVersion { EngineName = "6.5 V12 800 KM", PowerHP = 800, PowerKW = 588, Displacement = 6496, FuelTypeId = ben,
                    TorqueNm = 718, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 2.9m, TopSpeedKmh = 340, FuelConsumptionCombined = 17.0m },
            ]);

            int california = GetOrCreateModel(ferrId, "California", "ferrari-california");
            AddOrReplaceEngines(GetOrFixGeneration(california, "I (2008–2017)", "ferrari-california-i", 2008, 2017), 350, [
                new EngineVersion { EngineName = "4.3 V8 460 KM", PowerHP = 460, PowerKW = 338, Displacement = 4297, FuelTypeId = ben,
                    TorqueNm = 485, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 4.0m, TopSpeedKmh = 310, FuelConsumptionCombined = 12.6m },
                new EngineVersion { EngineName = "California T 4.0 Turbo 560 KM", PowerHP = 560, PowerKW = 412, Displacement = 3855, FuelTypeId = ben,
                    TorqueNm = 755, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.6m, TopSpeedKmh = 316, FuelConsumptionCombined = 10.5m },
            ]);

            int enzo = GetOrCreateModel(ferrId, "Enzo", "ferrari-enzo");
            AddOrReplaceEngines(GetOrFixGeneration(enzo, "I (2002–2004)", "ferrari-enzo-i", 2002, 2004), 600, [
                new EngineVersion { EngineName = "6.0 V12 660 KM", PowerHP = 660, PowerKW = 485, Displacement = 5998, FuelTypeId = ben,
                    TorqueNm = 657, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 3.6m, TopSpeedKmh = 350, FuelConsumptionCombined = 21.0m },
            ]);

            int f40 = GetOrCreateModel(ferrId, "F40", "ferrari-f40");
            AddOrReplaceEngines(GetOrFixGeneration(f40, "I (1987–1992)", "ferrari-f40-i", 1987, 1992), 400, [
                new EngineVersion { EngineName = "2.9 V8 Twin-Turbo 478 KM", PowerHP = 478, PowerKW = 352, Displacement = 2936, FuelTypeId = ben,
                    TorqueNm = 577, EuroNorm = "Euro 0", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 4.1m, TopSpeedKmh = 324, FuelConsumptionCombined = 19.5m },
            ]);

            int f50 = GetOrCreateModel(ferrId, "F50", "ferrari-f50");
            AddOrReplaceEngines(GetOrFixGeneration(f50, "I (1995–1997)", "ferrari-f50-i", 1995, 1997), 450, [
                new EngineVersion { EngineName = "4.7 V12 520 KM", PowerHP = 520, PowerKW = 382, Displacement = 4698, FuelTypeId = ben,
                    TorqueNm = 471, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 3.7m, TopSpeedKmh = 325, FuelConsumptionCombined = 21.0m },
            ]);

            int gtc4Lusso = GetOrCreateModel(ferrId, "GTC4Lusso", "ferrari-gtc4lusso");
            AddOrReplaceEngines(GetOrFixGeneration(gtc4Lusso, "I (2016–2020)", "ferrari-gtc4lusso-i", 2016, 2020), 550, [
                new EngineVersion { EngineName = "6.3 V12 690 KM", PowerHP = 690, PowerKW = 507, Displacement = 6262, FuelTypeId = ben,
                    TorqueNm = 697, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 12, Acceleration0100 = 3.4m, TopSpeedKmh = 335, FuelConsumptionCombined = 15.7m },
                new EngineVersion { EngineName = "GTC4Lusso T 3.9 Twin-Turbo V8 610 KM", PowerHP = 610, PowerKW = 448, Displacement = 3855, FuelTypeId = ben,
                    TorqueNm = 760, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.5m, TopSpeedKmh = 320, FuelConsumptionCombined = 11.6m },
            ]);

            int laFerrari = GetOrCreateModel(ferrId, "LaFerrari", "ferrari-laferrari");
            AddOrReplaceEngines(GetOrFixGeneration(laFerrari, "I (2013–2018)", "ferrari-laferrari-i", 2013, 2018), 900, [
                new EngineVersion { EngineName = "6.3 V12 Hybrid 963 KM", PowerHP = 963, PowerKW = 708, Displacement = 6262, FuelTypeId = phev,
                    TorqueNm = 900, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 2.6m, TopSpeedKmh = 350, FuelConsumptionCombined = 14.0m },
            ]);

            int testarossa = GetOrCreateModel(ferrId, "Testarossa", "ferrari-testarossa");
            AddOrReplaceEngines(GetOrFixGeneration(testarossa, "I (1984–1996)", "ferrari-testarossa-i", 1984, 1996), 350, [
                new EngineVersion { EngineName = "4.9 F12 390 KM", PowerHP = 390, PowerKW = 287, Displacement = 4942, FuelTypeId = ben,
                    TorqueNm = 490, EuroNorm = "Euro 1", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 12, Acceleration0100 = 5.2m, TopSpeedKmh = 290, FuelConsumptionCombined = 20.0m },
            ]);
        }

        // ── Tesla ─────────────────────────────────────────────────────────────────
        {
            int teslId = GetOrCreateBrand("Tesla", "tesla", "osobowe");
            int m3 = GetOrCreateModel(teslId, "Model 3", "tesla-model3");
            AddEngines(GetOrCreateGeneration(m3, "I (2017–2023)", "tesla-model3-i", 2017, 2023), [
                new EngineVersion { EngineName = "Standard Range RWD 283 KM", PowerHP = 283, PowerKW = 208, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 375, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "RWD",
                    Cylinders = 0, Acceleration0100 = 5.6m, TopSpeedKmh = 225, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Long Range AWD 498 KM", PowerHP = 498, PowerKW = 366, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 639, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 4.4m, TopSpeedKmh = 233, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Performance AWD 513 KM", PowerHP = 513, PowerKW = 377, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 660, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 3.3m, TopSpeedKmh = 261, FuelConsumptionCombined = null },
            ]);
            AddEngines(GetOrCreateGeneration(m3, "Highland (2023–)", "tesla-model3-highland", 2023, null), [
                new EngineVersion { EngineName = "RWD 299 KM", PowerHP = 299, PowerKW = 220, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 420, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "RWD",
                    Cylinders = 0, Acceleration0100 = 6.1m, TopSpeedKmh = 201, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Long Range AWD 544 KM", PowerHP = 544, PowerKW = 400, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 660, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 4.4m, TopSpeedKmh = 222, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Performance AWD 460 KM", PowerHP = 460, PowerKW = 338, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 730, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 3.1m, TopSpeedKmh = 262, FuelConsumptionCombined = null },
            ]);
            int my = GetOrCreateModel(teslId, "Model Y", "tesla-modely");
            AddEngines(GetOrCreateGeneration(my, "I (2020–)", "tesla-modely-i", 2020, null), [
                new EngineVersion { EngineName = "Standard Range RWD 299 KM", PowerHP = 299, PowerKW = 220, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 420, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "RWD",
                    Cylinders = 0, Acceleration0100 = 6.9m, TopSpeedKmh = 217, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Long Range AWD 514 KM", PowerHP = 514, PowerKW = 378, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 660, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 5.0m, TopSpeedKmh = 217, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Performance AWD 534 KM", PowerHP = 534, PowerKW = 393, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 660, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 3.7m, TopSpeedKmh = 250, FuelConsumptionCombined = null },
            ]);
            int ms = GetOrCreateModel(teslId, "Model S", "tesla-models");
            AddEngines(GetOrCreateGeneration(ms, "Plaid (2021–)", "tesla-models-plaid", 2021, null), [
                new EngineVersion { EngineName = "Long Range AWD 670 KM", PowerHP = 670, PowerKW = 493, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 1050, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 3.2m, TopSpeedKmh = 250, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Plaid 3×motor 1020 KM", PowerHP = 1020, PowerKW = 750, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 1420, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 2.1m, TopSpeedKmh = 322, FuelConsumptionCombined = null },
            ]);
            int mx = GetOrCreateModel(teslId, "Model X", "tesla-modelx");
            AddEngines(GetOrCreateGeneration(mx, "Plaid (2021–)", "tesla-modelx-plaid", 2021, null), [
                new EngineVersion { EngineName = "Long Range AWD 670 KM", PowerHP = 670, PowerKW = 493, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 1050, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 3.9m, TopSpeedKmh = 250, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Plaid 3×motor 1020 KM", PowerHP = 1020, PowerKW = 750, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 1310, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 2.6m, TopSpeedKmh = 262, FuelConsumptionCombined = null },
            ]);
        }

        // ── Alfa Romeo ────────────────────────────────────────────────────────────
        {
            int alfaId = GetOrCreateBrand("Alfa Romeo", "alfa-romeo", "osobowe");
            int giulia = GetOrCreateModel(alfaId, "Giulia", "alfa-romeo-giulia");
            AddEngines(GetOrCreateGeneration(giulia, "952 (2016–)", "alfa-giulia-952", 2016, null), [
                new EngineVersion { EngineName = "2.0 Turbo 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 1995, FuelTypeId = ben,
                    TorqueNm = 330, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 6.6m, TopSpeedKmh = 240, FuelConsumptionCombined = 6.4m },
                new EngineVersion { EngineName = "2.0 Turbo 280 KM", PowerHP = 280, PowerKW = 206, Displacement = 1995, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 5.2m, TopSpeedKmh = 250, FuelConsumptionCombined = 6.7m },
                new EngineVersion { EngineName = "2.9 V6 Bi-Turbo Quadrifoglio 510 KM", PowerHP = 510, PowerKW = 375, Displacement = 2891, FuelTypeId = ben,
                    TorqueNm = 600, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 3.9m, TopSpeedKmh = 307, FuelConsumptionCombined = 10.1m },
                new EngineVersion { EngineName = "2.2 JTDm 160 KM", PowerHP = 160, PowerKW = 118, Displacement = 2143, FuelTypeId = die,
                    TorqueNm = 360, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 8.2m, TopSpeedKmh = 220, FuelConsumptionCombined = 4.9m },
                new EngineVersion { EngineName = "2.2 JTDm 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 2143, FuelTypeId = die,
                    TorqueNm = 450, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 6.6m, TopSpeedKmh = 230, FuelConsumptionCombined = 5.0m },
            ]);
            int stelvio = GetOrCreateModel(alfaId, "Stelvio", "alfa-romeo-stelvio");
            AddEngines(GetOrCreateGeneration(stelvio, "949 (2017–)", "alfa-stelvio-949", 2017, null), [
                new EngineVersion { EngineName = "2.0 Turbo 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 1995, FuelTypeId = ben,
                    TorqueNm = 330, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.1m, TopSpeedKmh = 230, FuelConsumptionCombined = 7.2m },
                new EngineVersion { EngineName = "2.0 Turbo 280 KM", PowerHP = 280, PowerKW = 206, Displacement = 1995, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 5.7m, TopSpeedKmh = 240, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "2.9 V6 Quadrifoglio 510 KM", PowerHP = 510, PowerKW = 375, Displacement = 2891, FuelTypeId = ben,
                    TorqueNm = 600, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 3.8m, TopSpeedKmh = 283, FuelConsumptionCombined = 10.3m },
                new EngineVersion { EngineName = "2.2 JTDm 160 KM", PowerHP = 160, PowerKW = 118, Displacement = 2143, FuelTypeId = die,
                    TorqueNm = 360, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.0m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "2.2 JTDm 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 2143, FuelTypeId = die,
                    TorqueNm = 450, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.4m, TopSpeedKmh = 220, FuelConsumptionCombined = 5.7m },
            ]);
            int tonale = GetOrCreateModel(alfaId, "Tonale", "alfa-romeo-tonale");
            AddEngines(GetOrCreateGeneration(tonale, "I (2022–)", "alfa-tonale-i", 2022, null), [
                new EngineVersion { EngineName = "1.5 MHEV 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1469, FuelTypeId = mild,
                    TorqueNm = 240, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.4m, TopSpeedKmh = 200, FuelConsumptionCombined = 5.8m },
                new EngineVersion { EngineName = "1.5 MHEV 160 KM", PowerHP = 160, PowerKW = 118, Displacement = 1469, FuelTypeId = mild,
                    TorqueNm = 240, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.8m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "1.3 PHEV 280 KM AWD", PowerHP = 280, PowerKW = 206, Displacement = 1330, FuelTypeId = phev,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.2m, TopSpeedKmh = 220, FuelConsumptionCombined = 2.1m },
                new EngineVersion { EngineName = "1.6 JTDm 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.2m, TopSpeedKmh = 195, FuelConsumptionCombined = 5.2m },
            ]);
            int giulietta = GetOrCreateModel(alfaId, "Giulietta", "alfa-romeo-giulietta");
            PrepareGenerations(giulietta,
                ("I (2010–2016)", "alfa-giulietta-i", 2010, 2016),
                ("II (2016–2020)", "alfa-giulietta-ii", 2016, 2020));
            AddOrReplaceEngines(GetOrFixGeneration(giulietta, "I (2010–2016)", "alfa-giulietta-i", 2010, 2016), 100, [
                new EngineVersion { EngineName = "1.4 MultiAir Turbo 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 215, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 195, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "1.4 MultiAir Turbo 170 KM", PowerHP = 170, PowerKW = 125, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.7m, TopSpeedKmh = 218, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "1.75 TBi Quadrifoglio Verde 235 KM", PowerHP = 235, PowerKW = 173, Displacement = 1742, FuelTypeId = ben,
                    TorqueNm = 340, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.8m, TopSpeedKmh = 244, FuelConsumptionCombined = 8.2m },
                new EngineVersion { EngineName = "1.6 JTDm 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.7m, TopSpeedKmh = 185, FuelConsumptionCombined = 4.2m },
                new EngineVersion { EngineName = "2.0 JTDm 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1956, FuelTypeId = die,
                    TorqueNm = 350, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.2m, TopSpeedKmh = 206, FuelConsumptionCombined = 4.5m },
                new EngineVersion { EngineName = "2.0 JTDm 175 KM", PowerHP = 175, PowerKW = 129, Displacement = 1956, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.2m, TopSpeedKmh = 218, FuelConsumptionCombined = 4.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(giulietta, "II (2016–2020)", "alfa-giulietta-ii", 2016, 2020), 100, [
                new EngineVersion { EngineName = "1.4 MultiAir Turbo 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 215, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 195, FuelConsumptionCombined = 5.8m },
                new EngineVersion { EngineName = "1.4 MultiAir Turbo 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.3m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "1.75 TBi Veloce 240 KM", PowerHP = 240, PowerKW = 177, Displacement = 1742, FuelTypeId = ben,
                    TorqueNm = 340, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.6m, TopSpeedKmh = 246, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "1.6 JTDm 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.6m, TopSpeedKmh = 195, FuelConsumptionCombined = 4.1m },
                new EngineVersion { EngineName = "2.0 JTDm 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1956, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.8m, TopSpeedKmh = 210, FuelConsumptionCombined = 4.6m },
                new EngineVersion { EngineName = "2.0 JTDm 175 KM", PowerHP = 175, PowerKW = 129, Displacement = 1956, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 218, FuelConsumptionCombined = 4.7m },
            ]);
        }

        // ── Lexus ─────────────────────────────────────────────────────────────────
        {
            int lexId = GetOrCreateBrand("Lexus", "lexus", "osobowe");
            int isLex = GetOrCreateModel(lexId, "IS", "lexus-is");
            AddEngines(GetOrCreateGeneration(isLex, "III (2013–)", "lexus-is-iii", 2013, null), [
                new EngineVersion { EngineName = "2.0 Turbo 245 KM", PowerHP = 245, PowerKW = 180, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 220, FuelConsumptionCombined = 7.9m },
                new EngineVersion { EngineName = "3.5 V6 HEV 300 KM", PowerHP = 300, PowerKW = 220, Displacement = 3456, FuelTypeId = hyb,
                    TorqueNm = 335, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 8.3m, TopSpeedKmh = 200, FuelConsumptionCombined = 7.3m },
                new EngineVersion { EngineName = "3.5 V6 F Sport 315 KM", PowerHP = 315, PowerKW = 232, Displacement = 3456, FuelTypeId = ben,
                    TorqueNm = 380, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 6.0m, TopSpeedKmh = 270, FuelConsumptionCombined = 10.7m },
            ]);
            int nx = GetOrCreateModel(lexId, "NX", "lexus-nx");
            AddEngines(GetOrCreateGeneration(nx, "AZ10 (2014–2021)", "lexus-nx-az10", 2014, 2021), [
                new EngineVersion { EngineName = "2.0 Turbo 238 KM", PowerHP = 238, PowerKW = 175, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.9m, TopSpeedKmh = 190, FuelConsumptionCombined = 7.8m },
                new EngineVersion { EngineName = "2.5 HEV 197 KM", PowerHP = 197, PowerKW = 145, Displacement = 2494, FuelTypeId = hyb,
                    TorqueNm = 210, EuroNorm = "Euro 6", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.0m, TopSpeedKmh = 180, FuelConsumptionCombined = 5.9m },
            ]);
            AddEngines(GetOrCreateGeneration(nx, "AZ20 (2021–)", "lexus-nx-az20", 2021, null), [
                new EngineVersion { EngineName = "2.5 HEV 244 KM AWD", PowerHP = 244, PowerKW = 179, Displacement = 2487, FuelTypeId = hyb,
                    TorqueNm = 239, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.7m, TopSpeedKmh = 180, FuelConsumptionCombined = 5.3m },
                new EngineVersion { EngineName = "2.5 PHEV 306 KM AWD", PowerHP = 306, PowerKW = 225, Displacement = 2487, FuelTypeId = phev,
                    TorqueNm = 249, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.3m, TopSpeedKmh = 200, FuelConsumptionCombined = 1.4m },
            ]);
            int rx = GetOrCreateModel(lexId, "RX", "lexus-rx");
            AddEngines(GetOrCreateGeneration(rx, "IV (2015–2022)", "lexus-rx-iv", 2015, 2022), [
                new EngineVersion { EngineName = "2.0 Turbo 238 KM", PowerHP = 238, PowerKW = 175, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 200, FuelConsumptionCombined = 8.9m },
                new EngineVersion { EngineName = "3.5 HEV 313 KM AWD", PowerHP = 313, PowerKW = 230, Displacement = 3456, FuelTypeId = hyb,
                    TorqueNm = 335, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 7.7m, TopSpeedKmh = 200, FuelConsumptionCombined = 6.6m },
            ]);
            AddEngines(GetOrCreateGeneration(rx, "V (2022–)", "lexus-rx-v", 2022, null), [
                new EngineVersion { EngineName = "2.5 HEV 246 KM AWD", PowerHP = 246, PowerKW = 181, Displacement = 2487, FuelTypeId = hyb,
                    TorqueNm = 239, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 200, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "2.5 PHEV 309 KM AWD", PowerHP = 309, PowerKW = 227, Displacement = 2487, FuelTypeId = phev,
                    TorqueNm = 249, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.2m, TopSpeedKmh = 200, FuelConsumptionCombined = 1.4m },
                new EngineVersion { EngineName = "3.5 HEV 374 KM AWD", PowerHP = 374, PowerKW = 275, Displacement = 3456, FuelTypeId = hyb,
                    TorqueNm = 335, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.2m, TopSpeedKmh = 200, FuelConsumptionCombined = 7.2m },
            ]);
            int ux = GetOrCreateModel(lexId, "UX", "lexus-ux");
            AddEngines(GetOrCreateGeneration(ux, "ZA10 (2018–)", "lexus-ux-za10", 2018, null), [
                new EngineVersion { EngineName = "2.0 HEV 184 KM FWD", PowerHP = 184, PowerKW = 135, Displacement = 1987, FuelTypeId = hyb,
                    TorqueNm = 188, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 175, FuelConsumptionCombined = 4.9m },
                new EngineVersion { EngineName = "2.0 HEV 184 KM AWD", PowerHP = 184, PowerKW = 135, Displacement = 1987, FuelTypeId = hyb,
                    TorqueNm = 188, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.8m, TopSpeedKmh = 175, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "300e EV 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 0, Acceleration0100 = 7.5m, TopSpeedKmh = 160, FuelConsumptionCombined = null },
            ]);

            int es = GetOrCreateModel(lexId, "ES", "lexus-es");
            AddOrReplaceEngines(GetOrFixGeneration(es, "XZ10 (2018–)", "lexus-es-xz10", 2018, null), 170, [
                new EngineVersion { EngineName = "ES300h 2.5 HEV 218 KM", PowerHP = 218, PowerKW = 160, Displacement = 2487, FuelTypeId = hyb,
                    TorqueNm = 221, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.9m, TopSpeedKmh = 180, FuelConsumptionCombined = 4.8m },
            ]);

            int ls = GetOrCreateModel(lexId, "LS", "lexus-ls");
            AddOrReplaceEngines(GetOrFixGeneration(ls, "XF50 (2017–)", "lexus-ls-xf50", 2017, null), 250, [
                new EngineVersion { EngineName = "LS500 3.5T 415 KM", PowerHP = 415, PowerKW = 305, Displacement = 3444, FuelTypeId = ben,
                    TorqueNm = 600, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.0m, TopSpeedKmh = 250, FuelConsumptionCombined = 9.5m },
                new EngineVersion { EngineName = "LS500h 3.5 HEV 359 KM", PowerHP = 359, PowerKW = 264, Displacement = 3456, FuelTypeId = hyb,
                    TorqueNm = 348, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.5m, TopSpeedKmh = 250, FuelConsumptionCombined = 6.6m },
            ]);

            int lc = GetOrCreateModel(lexId, "LC", "lexus-lc");
            AddOrReplaceEngines(GetOrFixGeneration(lc, "XZ100 (2017–)", "lexus-lc-xz100", 2017, null), 350, [
                new EngineVersion { EngineName = "LC500 5.0 V8 477 KM", PowerHP = 477, PowerKW = 351, Displacement = 4969, FuelTypeId = ben,
                    TorqueNm = 540, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 4.7m, TopSpeedKmh = 270, FuelConsumptionCombined = 11.6m },
                new EngineVersion { EngineName = "LC500h 3.5 HEV 359 KM", PowerHP = 359, PowerKW = 264, Displacement = 3456, FuelTypeId = hyb,
                    TorqueNm = 348, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 5.0m, TopSpeedKmh = 250, FuelConsumptionCombined = 6.7m },
            ]);

            int lx = GetOrCreateModel(lexId, "LX", "lexus-lx");
            AddOrReplaceEngines(GetOrFixGeneration(lx, "URJ150 (2015–2021)", "lexus-lx-urj150", 2015, 2021), 250, [
                new EngineVersion { EngineName = "LX570 5.7 V8 367 KM", PowerHP = 367, PowerKW = 270, Displacement = 5663, FuelTypeId = ben,
                    TorqueNm = 530, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 6.9m, TopSpeedKmh = 210, FuelConsumptionCombined = 15.5m },
            ]);

            int rc = GetOrCreateModel(lexId, "RC", "lexus-rc");
            AddOrReplaceEngines(GetOrFixGeneration(rc, "I (2014–)", "lexus-rc-i", 2014, null), 220, [
                new EngineVersion { EngineName = "RC300h 2.5 HEV 223 KM", PowerHP = 223, PowerKW = 164, Displacement = 2494, FuelTypeId = hyb,
                    TorqueNm = 221, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 8.6m, TopSpeedKmh = 190, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "RC F 5.0 V8 477 KM", PowerHP = 477, PowerKW = 351, Displacement = 4969, FuelTypeId = ben,
                    TorqueNm = 535, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 4.5m, TopSpeedKmh = 270, FuelConsumptionCombined = 11.6m },
            ]);
        }

        // ── Maserati ──────────────────────────────────────────────────────────────
        {
            int masId = GetOrCreateBrand("Maserati", "maserati", "osobowe");
            int ghibli = GetOrCreateModel(masId, "Ghibli", "maserati-ghibli");
            AddEngines(GetOrCreateGeneration(ghibli, "M157 (2013–2023)", "maserati-ghibli-m157", 2013, 2023), [
                new EngineVersion { EngineName = "2.0 MHEV 330 KM", PowerHP = 330, PowerKW = 243, Displacement = 1998, FuelTypeId = mild,
                    TorqueNm = 450, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 5.7m, TopSpeedKmh = 255, FuelConsumptionCombined = 7.8m },
                new EngineVersion { EngineName = "3.0 V6 Turbo 350 KM", PowerHP = 350, PowerKW = 257, Displacement = 2979, FuelTypeId = ben,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.5m, TopSpeedKmh = 267, FuelConsumptionCombined = 10.2m },
                new EngineVersion { EngineName = "3.0 V6 Bi-Turbo S 430 KM", PowerHP = 430, PowerKW = 316, Displacement = 2979, FuelTypeId = ben,
                    TorqueNm = 580, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 4.7m, TopSpeedKmh = 286, FuelConsumptionCombined = 11.0m },
                new EngineVersion { EngineName = "3.0 V6 Diesel 275 KM", PowerHP = 275, PowerKW = 202, Displacement = 2987, FuelTypeId = die,
                    TorqueNm = 600, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.3m, TopSpeedKmh = 250, FuelConsumptionCombined = 6.2m },
            ]);
            int levante = GetOrCreateModel(masId, "Levante", "maserati-levante");
            AddEngines(GetOrCreateGeneration(levante, "M161 (2016–)", "maserati-levante-m161", 2016, null), [
                new EngineVersion { EngineName = "2.0 MHEV 330 KM", PowerHP = 330, PowerKW = 243, Displacement = 1998, FuelTypeId = mild,
                    TorqueNm = 450, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 5.8m, TopSpeedKmh = 255, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "3.0 V6 350 KM", PowerHP = 350, PowerKW = 257, Displacement = 2979, FuelTypeId = ben,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.0m, TopSpeedKmh = 264, FuelConsumptionCombined = 11.2m },
                new EngineVersion { EngineName = "3.0 V6 Trofeo 580 KM", PowerHP = 580, PowerKW = 427, Displacement = 2979, FuelTypeId = ben,
                    TorqueNm = 730, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 4.1m, TopSpeedKmh = 301, FuelConsumptionCombined = 12.8m },
                new EngineVersion { EngineName = "3.0 V6 Diesel 275 KM", PowerHP = 275, PowerKW = 202, Displacement = 2987, FuelTypeId = die,
                    TorqueNm = 600, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.9m, TopSpeedKmh = 240, FuelConsumptionCombined = 7.1m },
            ]);
            int gt = GetOrCreateModel(masId, "GranTurismo", "maserati-granturismo");
            AddEngines(GetOrCreateGeneration(gt, "M139 (2007–2019)", "maserati-gt-m139", 2007, 2019), [
                new EngineVersion { EngineName = "4.2 V8 405 KM", PowerHP = 405, PowerKW = 298, Displacement = 4244, FuelTypeId = ben,
                    TorqueNm = 460, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 5.2m, TopSpeedKmh = 285, FuelConsumptionCombined = 14.7m },
                new EngineVersion { EngineName = "4.7 V8 S 460 KM", PowerHP = 460, PowerKW = 338, Displacement = 4691, FuelTypeId = ben,
                    TorqueNm = 520, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 4.7m, TopSpeedKmh = 300, FuelConsumptionCombined = 15.4m },
            ]);
            AddEngines(GetOrCreateGeneration(gt, "M180 (2023–)", "maserati-gt-m180", 2023, null), [
                new EngineVersion { EngineName = "3.0 V6 Nettuno 490 KM", PowerHP = 490, PowerKW = 360, Displacement = 2992, FuelTypeId = ben,
                    TorqueNm = 620, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 4.0m, TopSpeedKmh = 302, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "Folgore EV 761 KM AWD", PowerHP = 761, PowerKW = 560, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 900, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 2.7m, TopSpeedKmh = 325, FuelConsumptionCombined = null },
            ]);

            int quattroporte = GetOrCreateModel(masId, "Quattroporte", "maserati-quattroporte");
            AddOrReplaceEngines(GetOrFixGeneration(quattroporte, "M156 (2013–2023)", "maserati-quattroporte-m156", 2013, 2023), 300, [
                new EngineVersion { EngineName = "3.0 V6 350 KM", PowerHP = 350, PowerKW = 257, Displacement = 2979, FuelTypeId = ben,
                    TorqueNm = 500, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 5.1m, TopSpeedKmh = 267, FuelConsumptionCombined = 12.5m },
                new EngineVersion { EngineName = "GTS 3.8 V8 530 KM", PowerHP = 530, PowerKW = 390, Displacement = 3799, FuelTypeId = ben,
                    TorqueNm = 710, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 4.7m, TopSpeedKmh = 310, FuelConsumptionCombined = 15.0m },
                new EngineVersion { EngineName = "3.0 V6 D 275 KM", PowerHP = 275, PowerKW = 202, Displacement = 2987, FuelTypeId = die,
                    TorqueNm = 600, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 6.4m, TopSpeedKmh = 250, FuelConsumptionCombined = 6.8m },
            ]);

            int mc20 = GetOrCreateModel(masId, "MC20", "maserati-mc20");
            AddOrReplaceEngines(GetOrFixGeneration(mc20, "I (2020–)", "maserati-mc20-i", 2020, null), 500, [
                new EngineVersion { EngineName = "3.0 V6 Nettuno 630 KM", PowerHP = 630, PowerKW = 463, Displacement = 2992, FuelTypeId = ben,
                    TorqueNm = 730, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 2.9m, TopSpeedKmh = 325, FuelConsumptionCombined = 11.5m },
            ]);

            int granCabrio = GetOrCreateModel(masId, "GranCabrio", "maserati-grancabrio");
            AddOrReplaceEngines(GetOrFixGeneration(granCabrio, "M180 (2024–)", "maserati-grancabrio-m180", 2024, null), 490, [
                new EngineVersion { EngineName = "3.0 V6 Nettuno 550 KM", PowerHP = 550, PowerKW = 404, Displacement = 2992, FuelTypeId = ben,
                    TorqueNm = 650, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 3.9m, TopSpeedKmh = 300, FuelConsumptionCombined = 10.9m },
                new EngineVersion { EngineName = "Folgore EV 761 KM AWD", PowerHP = 761, PowerKW = 560, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 900, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 2.8m, TopSpeedKmh = 289, FuelConsumptionCombined = null },
            ]);
        }

        // ── Citroën ───────────────────────────────────────────────────────────────
        {
            int citId = GetOrCreateBrand("Citroën", "citroen", "osobowe");
            int c3 = GetOrCreateModel(citId, "C3", "citroen-c3");
            AddEngines(GetOrCreateGeneration(c3, "III (2016–)", "citroen-c3-iii", 2016, null), [
                new EngineVersion { EngineName = "1.2 PureTech 83 KM", PowerHP = 83, PowerKW = 61, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 118, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 13.3m, TopSpeedKmh = 168, FuelConsumptionCombined = 5.1m },
                new EngineVersion { EngineName = "1.2 PureTech 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 205, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.5m, TopSpeedKmh = 186, FuelConsumptionCombined = 5.3m },
                new EngineVersion { EngineName = "1.5 BlueHDi 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.2m, TopSpeedKmh = 183, FuelConsumptionCombined = 3.6m },
            ]);
            int c5air = GetOrCreateModel(citId, "C5 Aircross", "citroen-c5-aircross");
            AddEngines(GetOrCreateGeneration(c5air, "I (2018–)", "citroen-c5-aircross-i", 2018, null), [
                new EngineVersion { EngineName = "1.2 PureTech 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.2m, TopSpeedKmh = 195, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "1.6 PHEV 225 KM AWD", PowerHP = 225, PowerKW = 165, Displacement = 1598, FuelTypeId = phev,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.7m, TopSpeedKmh = 225, FuelConsumptionCombined = 1.5m },
                new EngineVersion { EngineName = "2.0 BlueHDi 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.0m, TopSpeedKmh = 215, FuelConsumptionCombined = 4.9m },
            ]);
            int c4 = GetOrCreateModel(citId, "C4", "citroen-c4");
            AddEngines(GetOrCreateGeneration(c4, "IV (2020–)", "citroen-c4-iv", 2020, null), [
                new EngineVersion { EngineName = "1.2 PureTech 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.9m, TopSpeedKmh = 200, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "ë-C4 EV 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 260, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 0, Acceleration0100 = 9.7m, TopSpeedKmh = 150, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "1.5 BlueHDi 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 188, FuelConsumptionCombined = 4.1m },
            ]);
            int berlingo = GetOrCreateModel(citId, "Berlingo", "citroen-berlingo");
            AddEngines(GetOrCreateGeneration(berlingo, "III (2018–)", "citroen-berlingo-iii", 2018, null), [
                new EngineVersion { EngineName = "1.2 PureTech 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 205, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 12.5m, TopSpeedKmh = 180, FuelConsumptionCombined = 5.7m },
                new EngineVersion { EngineName = "ë-Berlingo EV 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 260, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 0, Acceleration0100 = 11.7m, TopSpeedKmh = 135, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "1.5 BlueHDi 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.0m, TopSpeedKmh = 177, FuelConsumptionCombined = 4.2m },
                new EngineVersion { EngineName = "1.5 BlueHDi 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.2m, TopSpeedKmh = 185, FuelConsumptionCombined = 4.1m },
            ]);

            int c1 = GetOrCreateModel(citId, "C1", "citroen-c1");
            PrepareGenerations(c1,
                ("I (2005–2014)", "citroen-c1-i", 2005, 2014),
                ("II (2014–)", "citroen-c1-ii", 2014, null));
            AddOrReplaceEngines(GetOrFixGeneration(c1, "I (2005–2014)", "citroen-c1-i", 2005, 2014), 50, [
                new EngineVersion { EngineName = "1.0 68 KM", PowerHP = 68, PowerKW = 50, Displacement = 998, FuelTypeId = ben,
                    TorqueNm = 93, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 14.2m, TopSpeedKmh = 156, FuelConsumptionCombined = 4.6m },
                new EngineVersion { EngineName = "1.4 HDi 54 KM", PowerHP = 54, PowerKW = 40, Displacement = 1398, FuelTypeId = die,
                    TorqueNm = 96, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 16.8m, TopSpeedKmh = 150, FuelConsumptionCombined = 3.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(c1, "II (2014–)", "citroen-c1-ii", 2014, null), 50, [
                new EngineVersion { EngineName = "1.0 68 KM", PowerHP = 68, PowerKW = 50, Displacement = 998, FuelTypeId = ben,
                    TorqueNm = 93, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 14.3m, TopSpeedKmh = 158, FuelConsumptionCombined = 4.3m },
                new EngineVersion { EngineName = "1.2 PureTech 82 KM", PowerHP = 82, PowerKW = 60, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 118, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.0m, TopSpeedKmh = 173, FuelConsumptionCombined = 4.9m },
            ]);

            int c2 = GetOrCreateModel(citId, "C2", "citroen-c2");
            AddOrReplaceEngines(GetOrFixGeneration(c2, "I (2003–2009)", "citroen-c2-i", 2003, 2009), 55, [
                new EngineVersion { EngineName = "1.1 60 KM", PowerHP = 60, PowerKW = 44, Displacement = 1124, FuelTypeId = ben,
                    TorqueNm = 96, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 15.8m, TopSpeedKmh = 155, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "1.4 75 KM", PowerHP = 75, PowerKW = 55, Displacement = 1360, FuelTypeId = ben,
                    TorqueNm = 118, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.8m, TopSpeedKmh = 165, FuelConsumptionCombined = 6.3m },
                new EngineVersion { EngineName = "1.6 VTS 125 KM", PowerHP = 125, PowerKW = 92, Displacement = 1587, FuelTypeId = ben,
                    TorqueNm = 147, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.3m, TopSpeedKmh = 200, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "1.4 HDi 70 KM", PowerHP = 70, PowerKW = 51, Displacement = 1398, FuelTypeId = die,
                    TorqueNm = 160, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 15.5m, TopSpeedKmh = 158, FuelConsumptionCombined = 4.3m },
            ]);

            int c3air = GetOrCreateModel(citId, "C3 Aircross", "citroen-c3-aircross");
            AddOrReplaceEngines(GetOrFixGeneration(c3air, "I (2017–)", "citroen-c3-aircross-i", 2017, null), 90, [
                new EngineVersion { EngineName = "1.2 PureTech 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 205, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.2m, TopSpeedKmh = 187, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "1.2 PureTech 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.6m, TopSpeedKmh = 192, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "1.5 BlueHDi 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.6m, TopSpeedKmh = 178, FuelConsumptionCombined = 4.4m },
                new EngineVersion { EngineName = "1.5 BlueHDi 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.6m, TopSpeedKmh = 188, FuelConsumptionCombined = 4.5m },
            ]);

            int c3picasso = GetOrCreateModel(citId, "C3 Picasso", "citroen-c3-picasso");
            AddOrReplaceEngines(GetOrFixGeneration(c3picasso, "I (2009–2017)", "citroen-c3-picasso-i", 2009, 2017), 90, [
                new EngineVersion { EngineName = "1.4 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 1360, FuelTypeId = ben,
                    TorqueNm = 130, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.5m, TopSpeedKmh = 170, FuelConsumptionCombined = 6.6m },
                new EngineVersion { EngineName = "1.6 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 160, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.9m, TopSpeedKmh = 185, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "1.6 HDi 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1560, FuelTypeId = die,
                    TorqueNm = 215, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.0m, TopSpeedKmh = 172, FuelConsumptionCombined = 4.5m },
                new EngineVersion { EngineName = "1.6 HDi 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1560, FuelTypeId = die,
                    TorqueNm = 270, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 182, FuelConsumptionCombined = 4.7m },
            ]);

            int c4picasso = GetOrCreateModel(citId, "C4 Picasso", "citroen-c4-picasso");
            PrepareGenerations(c4picasso,
                ("I (2006–2013)", "citroen-c4-picasso-i", 2006, 2013),
                ("II (2013–2018)", "citroen-c4-picasso-ii", 2013, 2018));
            AddOrReplaceEngines(GetOrFixGeneration(c4picasso, "I (2006–2013)", "citroen-c4-picasso-i", 2006, 2013), 90, [
                new EngineVersion { EngineName = "1.6 16V 109 KM", PowerHP = 109, PowerKW = 80, Displacement = 1587, FuelTypeId = ben,
                    TorqueNm = 147, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.9m, TopSpeedKmh = 180, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "2.0 16V 143 KM", PowerHP = 143, PowerKW = 105, Displacement = 1997, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.4m, TopSpeedKmh = 197, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "1.6 HDi 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 1560, FuelTypeId = die,
                    TorqueNm = 240, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.0m, TopSpeedKmh = 185, FuelConsumptionCombined = 5.3m },
                new EngineVersion { EngineName = "2.0 HDi 138 KM", PowerHP = 138, PowerKW = 101, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.6m, TopSpeedKmh = 200, FuelConsumptionCombined = 5.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(c4picasso, "II (2013–2018)", "citroen-c4-picasso-ii", 2013, 2018), 110, [
                new EngineVersion { EngineName = "1.2 PureTech 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.9m, TopSpeedKmh = 188, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "1.6 THP 165 KM", PowerHP = 165, PowerKW = 121, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 240, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.9m, TopSpeedKmh = 210, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "1.6 BlueHDi 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1560, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 190, FuelConsumptionCombined = 4.1m },
                new EngineVersion { EngineName = "2.0 BlueHDi 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 370, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.3m, TopSpeedKmh = 200, FuelConsumptionCombined = 4.6m },
            ]);

            int c5 = GetOrCreateModel(citId, "C5", "citroen-c5");
            PrepareGenerations(c5,
                ("I (2001–2008)", "citroen-c5-i", 2001, 2008),
                ("II (2008–2017)", "citroen-c5-ii", 2008, 2017));
            AddOrReplaceEngines(GetOrFixGeneration(c5, "I (2001–2008)", "citroen-c5-i", 2001, 2008), 100, [
                new EngineVersion { EngineName = "1.8 16V 116 KM", PowerHP = 116, PowerKW = 85, Displacement = 1749, FuelTypeId = ben,
                    TorqueNm = 165, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.9m, TopSpeedKmh = 188, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "2.0 16V 143 KM", PowerHP = 143, PowerKW = 105, Displacement = 1997, FuelTypeId = ben,
                    TorqueNm = 202, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.3m, TopSpeedKmh = 205, FuelConsumptionCombined = 9.2m },
                new EngineVersion { EngineName = "3.0 V6 210 KM", PowerHP = 210, PowerKW = 154, Displacement = 2946, FuelTypeId = ben,
                    TorqueNm = 285, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 8.7m, TopSpeedKmh = 225, FuelConsumptionCombined = 11.5m },
                new EngineVersion { EngineName = "2.0 HDi 109 KM", PowerHP = 109, PowerKW = 80, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.5m, TopSpeedKmh = 185, FuelConsumptionCombined = 5.8m },
                new EngineVersion { EngineName = "2.2 HDi 133 KM", PowerHP = 133, PowerKW = 98, Displacement = 2179, FuelTypeId = die,
                    TorqueNm = 315, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 200, FuelConsumptionCombined = 6.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(c5, "II (2008–2017)", "citroen-c5-ii", 2008, 2017), 100, [
                new EngineVersion { EngineName = "1.6 THP 156 KM", PowerHP = 156, PowerKW = 115, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 240, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 210, FuelConsumptionCombined = 7.8m },
                new EngineVersion { EngineName = "2.0 16V 143 KM", PowerHP = 143, PowerKW = 105, Displacement = 1997, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.7m, TopSpeedKmh = 205, FuelConsumptionCombined = 8.7m },
                new EngineVersion { EngineName = "2.0 HDi 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.8m, TopSpeedKmh = 202, FuelConsumptionCombined = 5.3m },
                new EngineVersion { EngineName = "2.2 HDi 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 2179, FuelTypeId = die,
                    TorqueNm = 450, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.9m, TopSpeedKmh = 225, FuelConsumptionCombined = 6.1m },
            ]);

            int c6 = GetOrCreateModel(citId, "C6", "citroen-c6");
            AddOrReplaceEngines(GetOrFixGeneration(c6, "I (2005–2012)", "citroen-c6-i", 2005, 2012), 150, [
                new EngineVersion { EngineName = "3.0 V6 211 KM", PowerHP = 211, PowerKW = 155, Displacement = 2946, FuelTypeId = ben,
                    TorqueNm = 285, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 9.3m, TopSpeedKmh = 227, FuelConsumptionCombined = 11.8m },
                new EngineVersion { EngineName = "2.7 HDi V6 208 KM", PowerHP = 208, PowerKW = 153, Displacement = 2720, FuelTypeId = die,
                    TorqueNm = 450, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 9.2m, TopSpeedKmh = 222, FuelConsumptionCombined = 7.8m },
                new EngineVersion { EngineName = "2.2 HDi 170 KM", PowerHP = 170, PowerKW = 125, Displacement = 2179, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.4m, TopSpeedKmh = 210, FuelConsumptionCombined = 6.5m },
            ]);

            int grandC4Picasso = GetOrCreateModel(citId, "Grand C4 Picasso", "citroen-grand-c4-picasso");
            PrepareGenerations(grandC4Picasso,
                ("I (2006–2013)", "citroen-grand-c4-picasso-i", 2006, 2013),
                ("II (2013–2018)", "citroen-grand-c4-picasso-ii", 2013, 2018));
            AddOrReplaceEngines(GetOrFixGeneration(grandC4Picasso, "I (2006–2013)", "citroen-grand-c4-picasso-i", 2006, 2013), 90, [
                new EngineVersion { EngineName = "1.6 16V 109 KM", PowerHP = 109, PowerKW = 80, Displacement = 1587, FuelTypeId = ben,
                    TorqueNm = 147, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.5m, TopSpeedKmh = 178, FuelConsumptionCombined = 7.8m },
                new EngineVersion { EngineName = "2.0 16V 143 KM", PowerHP = 143, PowerKW = 105, Displacement = 1997, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.9m, TopSpeedKmh = 195, FuelConsumptionCombined = 8.8m },
                new EngineVersion { EngineName = "1.6 HDi 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 1560, FuelTypeId = die,
                    TorqueNm = 240, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.6m, TopSpeedKmh = 183, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "2.0 HDi 138 KM", PowerHP = 138, PowerKW = 101, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 198, FuelConsumptionCombined = 6.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(grandC4Picasso, "II (2013–2018)", "citroen-grand-c4-picasso-ii", 2013, 2018), 110, [
                new EngineVersion { EngineName = "1.2 PureTech 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.4m, TopSpeedKmh = 185, FuelConsumptionCombined = 5.8m },
                new EngineVersion { EngineName = "1.6 THP 165 KM", PowerHP = 165, PowerKW = 121, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 240, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.3m, TopSpeedKmh = 207, FuelConsumptionCombined = 6.7m },
                new EngineVersion { EngineName = "1.6 BlueHDi 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1560, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 188, FuelConsumptionCombined = 4.3m },
                new EngineVersion { EngineName = "2.0 BlueHDi 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 370, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.7m, TopSpeedKmh = 198, FuelConsumptionCombined = 4.8m },
            ]);

            int jumper = GetOrCreateModel(citId, "Jumper", "citroen-jumper");
            PrepareGenerations(jumper,
                ("I (2006–2014)", "citroen-jumper-i", 2006, 2014),
                ("II (2014–)", "citroen-jumper-ii", 2014, null));
            AddOrReplaceEngines(GetOrFixGeneration(jumper, "I (2006–2014)", "citroen-jumper-i", 2006, 2014), 90, [
                new EngineVersion { EngineName = "2.2 HDi 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 2198, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 16.0m, TopSpeedKmh = 150, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "2.2 HDi 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 2198, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.5m, TopSpeedKmh = 165, FuelConsumptionCombined = 8.3m },
                new EngineVersion { EngineName = "3.0 HDi 157 KM", PowerHP = 157, PowerKW = 115, Displacement = 2999, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 175, FuelConsumptionCombined = 9.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(jumper, "II (2014–)", "citroen-jumper-ii", 2014, null), 100, [
                new EngineVersion { EngineName = "2.0 BlueHDi 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.5m, TopSpeedKmh = 158, FuelConsumptionCombined = 7.8m },
                new EngineVersion { EngineName = "2.2 BlueHDi 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 2179, FuelTypeId = die,
                    TorqueNm = 350, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.5m, TopSpeedKmh = 168, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "2.2 BlueHDi 165 KM", PowerHP = 165, PowerKW = 121, Displacement = 2179, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 175, FuelConsumptionCombined = 8.2m },
            ]);

            int jumpy = GetOrCreateModel(citId, "Jumpy", "citroen-jumpy");
            PrepareGenerations(jumpy,
                ("I (1994–2006)", "citroen-jumpy-i", 1994, 2006),
                ("II (2007–2016)", "citroen-jumpy-ii", 2007, 2016),
                ("III (2016–)", "citroen-jumpy-iii", 2016, null));
            AddOrReplaceEngines(GetOrFixGeneration(jumpy, "I (1994–2006)", "citroen-jumpy-i", 1994, 2006), 60, [
                new EngineVersion { EngineName = "1.9D 71 KM", PowerHP = 71, PowerKW = 52, Displacement = 1905, FuelTypeId = die,
                    TorqueNm = 127, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 19.0m, TopSpeedKmh = 140, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "2.0 HDi 94 KM", PowerHP = 94, PowerKW = 69, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 205, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 15.5m, TopSpeedKmh = 158, FuelConsumptionCombined = 7.2m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(jumpy, "II (2007–2016)", "citroen-jumpy-ii", 2007, 2016), 80, [
                new EngineVersion { EngineName = "1.6 HDi 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1560, FuelTypeId = die,
                    TorqueNm = 215, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 15.5m, TopSpeedKmh = 160, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "2.0 HDi 128 KM", PowerHP = 128, PowerKW = 94, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.9m, TopSpeedKmh = 178, FuelConsumptionCombined = 6.8m },
                new EngineVersion { EngineName = "2.0 HDi 163 KM", PowerHP = 163, PowerKW = 120, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 340, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 188, FuelConsumptionCombined = 7.2m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(jumpy, "III (2016–)", "citroen-jumpy-iii", 2016, null), 100, [
                new EngineVersion { EngineName = "1.5 BlueHDi 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.5m, TopSpeedKmh = 175, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "2.0 BlueHDi 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 370, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 185, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "2.0 BlueHDi 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.5m, TopSpeedKmh = 190, FuelConsumptionCombined = 6.3m },
            ]);

            int saxo = GetOrCreateModel(citId, "Saxo", "citroen-saxo");
            AddOrReplaceEngines(GetOrFixGeneration(saxo, "I (1996–2004)", "citroen-saxo-i", 1996, 2004), 40, [
                new EngineVersion { EngineName = "1.0 44 KM", PowerHP = 44, PowerKW = 32, Displacement = 954, FuelTypeId = ben,
                    TorqueNm = 76, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 17.5m, TopSpeedKmh = 140, FuelConsumptionCombined = 5.8m },
                new EngineVersion { EngineName = "1.1 60 KM", PowerHP = 60, PowerKW = 44, Displacement = 1124, FuelTypeId = ben,
                    TorqueNm = 89, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.5m, TopSpeedKmh = 155, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "1.6 VTS 118 KM", PowerHP = 118, PowerKW = 87, Displacement = 1587, FuelTypeId = ben,
                    TorqueNm = 148, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.9m, TopSpeedKmh = 195, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "1.5D 57 KM", PowerHP = 57, PowerKW = 42, Displacement = 1527, FuelTypeId = die,
                    TorqueNm = 108, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 17.5m, TopSpeedKmh = 145, FuelConsumptionCombined = 4.8m },
            ]);

            int spaceTourer = GetOrCreateModel(citId, "SpaceTourer", "citroen-spacetourer");
            AddOrReplaceEngines(GetOrFixGeneration(spaceTourer, "I (2016–)", "citroen-spacetourer-i", 2016, null), 100, [
                new EngineVersion { EngineName = "1.5 BlueHDi 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.0m, TopSpeedKmh = 178, FuelConsumptionCombined = 5.8m },
                new EngineVersion { EngineName = "2.0 BlueHDi 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 370, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 188, FuelConsumptionCombined = 6.3m },
                new EngineVersion { EngineName = "2.0 BlueHDi 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.0m, TopSpeedKmh = 195, FuelConsumptionCombined = 6.6m },
            ]);

            int xsara = GetOrCreateModel(citId, "Xsara", "citroen-xsara");
            AddOrReplaceEngines(GetOrFixGeneration(xsara, "I (1997–2006)", "citroen-xsara-i", 1997, 2006), 60, [
                new EngineVersion { EngineName = "1.4 75 KM", PowerHP = 75, PowerKW = 55, Displacement = 1360, FuelTypeId = ben,
                    TorqueNm = 118, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.5m, TopSpeedKmh = 165, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "1.6 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1587, FuelTypeId = ben,
                    TorqueNm = 132, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 180, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "1.8 101 KM", PowerHP = 101, PowerKW = 74, Displacement = 1761, FuelTypeId = ben,
                    TorqueNm = 152, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.8m, TopSpeedKmh = 187, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "2.0 VTS 167 KM", PowerHP = 167, PowerKW = 123, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 190, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.8m, TopSpeedKmh = 210, FuelConsumptionCombined = 8.8m },
                new EngineVersion { EngineName = "1.9D 69 KM", PowerHP = 69, PowerKW = 51, Displacement = 1905, FuelTypeId = die,
                    TorqueNm = 122, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 17.0m, TopSpeedKmh = 155, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "2.0 HDi 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 205, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.5m, TopSpeedKmh = 175, FuelConsumptionCombined = 5.2m },
            ]);
        }

        // ── Mini ──────────────────────────────────────────────────────────────────
        {
            int miniId = GetOrCreateBrand("Mini", "mini", "osobowe");
            int cooper = GetOrCreateModel(miniId, "Cooper", "mini-cooper");
            AddEngines(GetOrCreateGeneration(cooper, "F55/F56 (2014–2024)", "mini-cooper-f56", 2014, 2024), [
                new EngineVersion { EngineName = "1.5 TwinPower Turbo One 102 KM", PowerHP = 102, PowerKW = 75, Displacement = 1499, FuelTypeId = ben,
                    TorqueNm = 180, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.9m, TopSpeedKmh = 183, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "2.0 TwinPower Turbo S 178 KM", PowerHP = 178, PowerKW = 131, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 280, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.8m, TopSpeedKmh = 235, FuelConsumptionCombined = 6.7m },
                new EngineVersion { EngineName = "2.0 JCW 231 KM", PowerHP = 231, PowerKW = 170, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.3m, TopSpeedKmh = 246, FuelConsumptionCombined = 7.3m },
                new EngineVersion { EngineName = "1.5 Cooper D 116 KM", PowerHP = 116, PowerKW = 85, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 270, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.0m, TopSpeedKmh = 193, FuelConsumptionCombined = 3.9m },
                new EngineVersion { EngineName = "SE PHEV 224 KM", PowerHP = 224, PowerKW = 165, Displacement = 1499, FuelTypeId = phev,
                    TorqueNm = 385, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 6.7m, TopSpeedKmh = 210, FuelConsumptionCombined = 1.7m },
            ]);
            AddEngines(GetOrCreateGeneration(cooper, "J01 EV (2024–)", "mini-cooper-j01", 2024, null), [
                new EngineVersion { EngineName = "E EV 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 290, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 0, Acceleration0100 = 7.3m, TopSpeedKmh = 160, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "SE EV 218 KM", PowerHP = 218, PowerKW = 160, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 330, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 0, Acceleration0100 = 6.7m, TopSpeedKmh = 170, FuelConsumptionCombined = null },
            ]);
            int countryman = GetOrCreateModel(miniId, "Countryman", "mini-countryman");
            AddEngines(GetOrCreateGeneration(countryman, "F60 (2017–2023)", "mini-countryman-f60", 2017, 2023), [
                new EngineVersion { EngineName = "1.5 TwinPower Cooper 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1499, FuelTypeId = ben,
                    TorqueNm = 220, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.6m, TopSpeedKmh = 201, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "2.0 Cooper S ALL4 192 KM", PowerHP = 192, PowerKW = 141, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 280, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.4m, TopSpeedKmh = 222, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "2.0 JCW ALL4 306 KM", PowerHP = 306, PowerKW = 225, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 450, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 5.1m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.2m },
                new EngineVersion { EngineName = "SE PHEV ALL4 224 KM", PowerHP = 224, PowerKW = 165, Displacement = 1499, FuelTypeId = phev,
                    TorqueNm = 385, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 3, Acceleration0100 = 6.8m, TopSpeedKmh = 200, FuelConsumptionCombined = 2.1m },
                new EngineVersion { EngineName = "2.0 Cooper D ALL4 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1995, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.6m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.1m },
            ]);
            AddEngines(GetOrCreateGeneration(countryman, "U25 (2023–)", "mini-countryman-u25", 2023, null), [
                new EngineVersion { EngineName = "C 170 KM", PowerHP = 170, PowerKW = 125, Displacement = 1499, FuelTypeId = ben,
                    TorqueNm = 280, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 8.6m, TopSpeedKmh = 220, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "S ALL4 300 KM", PowerHP = 300, PowerKW = 220, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 5.6m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "SE EV 204 KM FWD", PowerHP = 204, PowerKW = 150, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 247, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 0, Acceleration0100 = 8.6m, TopSpeedKmh = 170, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "SE ALL4 EV 313 KM AWD", PowerHP = 313, PowerKW = 230, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 494, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 5.4m, TopSpeedKmh = 180, FuelConsumptionCombined = null },
            ]);
            int clubman = GetOrCreateModel(miniId, "Clubman", "mini-clubman");
            AddEngines(GetOrCreateGeneration(clubman, "F54 (2015–2024)", "mini-clubman-f54", 2015, 2024), [
                new EngineVersion { EngineName = "1.5 Cooper 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1499, FuelTypeId = ben,
                    TorqueNm = 220, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.4m, TopSpeedKmh = 210, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "2.0 Cooper S 192 KM", PowerHP = 192, PowerKW = 141, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 280, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.1m, TopSpeedKmh = 232, FuelConsumptionCombined = 6.7m },
                new EngineVersion { EngineName = "2.0 JCW ALL4 306 KM", PowerHP = 306, PowerKW = 225, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 450, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 4.9m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.2m },
                new EngineVersion { EngineName = "2.0 Cooper D 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1995, FuelTypeId = die,
                    TorqueNm = 330, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.6m, TopSpeedKmh = 215, FuelConsumptionCombined = 4.3m },
            ]);

            int aceman = GetOrCreateModel(miniId, "Aceman", "mini-aceman");
            AddOrReplaceEngines(GetOrFixGeneration(aceman, "I (2024–)", "mini-aceman-i", 2024, null), 150, [
                new EngineVersion { EngineName = "E 42.5 kWh 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 290, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 0, Acceleration0100 = 7.9m, TopSpeedKmh = 160, FuelConsumptionCombined = 15.6m },
                new EngineVersion { EngineName = "SE 54.2 kWh 218 KM", PowerHP = 218, PowerKW = 160, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 330, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 0, Acceleration0100 = 7.1m, TopSpeedKmh = 170, FuelConsumptionCombined = 16.5m },
            ]);

            int electric = GetOrCreateModel(miniId, "Electric", "mini-electric");
            AddOrReplaceEngines(GetOrFixGeneration(electric, "I (2020–2024)", "mini-electric-i", 2020, 2024), 150, [
                new EngineVersion { EngineName = "Cooper SE 32.6 kWh 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 270, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 0, Acceleration0100 = 7.3m, TopSpeedKmh = 150, FuelConsumptionCombined = 16.9m },
            ]);
        }

        // ── Jeep ──────────────────────────────────────────────────────────────────
        {
            int jeepId = GetOrCreateBrand("Jeep", "jeep", "osobowe");
            int compass = GetOrCreateModel(jeepId, "Compass", "jeep-compass");
            AddEngines(GetOrCreateGeneration(compass, "MP (2016–)", "jeep-compass-mp", 2016, null), [
                new EngineVersion { EngineName = "1.3 GSE T4 FWD 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1332, FuelTypeId = ben,
                    TorqueNm = 190, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 185, FuelConsumptionCombined = 6.3m },
                new EngineVersion { EngineName = "1.3 GSE T4 150 KM DCT", PowerHP = 150, PowerKW = 110, Displacement = 1332, FuelTypeId = ben,
                    TorqueNm = 270, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.6m, TopSpeedKmh = 192, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "4xe PHEV 240 KM AWD", PowerHP = 240, PowerKW = 177, Displacement = 1332, FuelTypeId = phev,
                    TorqueNm = 270, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 200, FuelConsumptionCombined = 2.0m },
                new EngineVersion { EngineName = "2.0 MultiJet AWD 170 KM", PowerHP = 170, PowerKW = 125, Displacement = 1956, FuelTypeId = die,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 10.0m, TopSpeedKmh = 200, FuelConsumptionCombined = 5.5m },
            ]);
            int renegade = GetOrCreateModel(jeepId, "Renegade", "jeep-renegade");
            AddEngines(GetOrCreateGeneration(renegade, "BU (2014–)", "jeep-renegade-bu", 2014, null), [
                new EngineVersion { EngineName = "1.0 GSE T3 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 190, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.5m, TopSpeedKmh = 174, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "1.3 GSE T4 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1332, FuelTypeId = ben,
                    TorqueNm = 270, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.9m, TopSpeedKmh = 188, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "4xe PHEV 240 KM AWD", PowerHP = 240, PowerKW = 177, Displacement = 1332, FuelTypeId = phev,
                    TorqueNm = 270, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.1m, TopSpeedKmh = 190, FuelConsumptionCombined = 1.8m },
                new EngineVersion { EngineName = "1.6 MultiJet 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.4m, TopSpeedKmh = 183, FuelConsumptionCombined = 4.3m },
            ]);
            int wrangler = GetOrCreateModel(jeepId, "Wrangler", "jeep-wrangler");
            AddEngines(GetOrCreateGeneration(wrangler, "JL (2018–)", "jeep-wrangler-jl", 2018, null), [
                new EngineVersion { EngineName = "2.0 Turbo 272 KM", PowerHP = 272, PowerKW = 200, Displacement = 1995, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.7m, TopSpeedKmh = 180, FuelConsumptionCombined = 10.6m },
                new EngineVersion { EngineName = "3.6 V6 Pentastar 285 KM", PowerHP = 285, PowerKW = 209, Displacement = 3604, FuelTypeId = ben,
                    TorqueNm = 353, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 8.6m, TopSpeedKmh = 183, FuelConsumptionCombined = 12.2m },
                new EngineVersion { EngineName = "4xe PHEV 380 KM", PowerHP = 380, PowerKW = 279, Displacement = 1995, FuelTypeId = phev,
                    TorqueNm = 637, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.4m, TopSpeedKmh = 177, FuelConsumptionCombined = 3.6m },
                new EngineVersion { EngineName = "2.2 MultiJet 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 2184, FuelTypeId = die,
                    TorqueNm = 450, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 10.7m, TopSpeedKmh = 175, FuelConsumptionCombined = 7.9m },
            ]);
            int gc = GetOrCreateModel(jeepId, "Grand Cherokee", "jeep-grand-cherokee");
            AddEngines(GetOrCreateGeneration(gc, "WK2 (2010–2021)", "jeep-gc-wk2", 2010, 2021), [
                new EngineVersion { EngineName = "3.6 V6 Pentastar 286 KM", PowerHP = 286, PowerKW = 210, Displacement = 3604, FuelTypeId = ben,
                    TorqueNm = 347, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 8.1m, TopSpeedKmh = 195, FuelConsumptionCombined = 11.9m },
                new EngineVersion { EngineName = "5.7 V8 Hemi 360 KM", PowerHP = 360, PowerKW = 265, Displacement = 5654, FuelTypeId = ben,
                    TorqueNm = 530, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 6.7m, TopSpeedKmh = 210, FuelConsumptionCombined = 14.0m },
                new EngineVersion { EngineName = "6.4 V8 SRT 468 KM", PowerHP = 468, PowerKW = 344, Displacement = 6417, FuelTypeId = ben,
                    TorqueNm = 624, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.8m, TopSpeedKmh = 249, FuelConsumptionCombined = 16.6m },
                new EngineVersion { EngineName = "3.0 CRD V6 250 KM", PowerHP = 250, PowerKW = 184, Displacement = 2987, FuelTypeId = die,
                    TorqueNm = 570, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 7.5m, TopSpeedKmh = 210, FuelConsumptionCombined = 7.5m },
            ]);
            AddEngines(GetOrCreateGeneration(gc, "WL (2021–)", "jeep-gc-wl", 2021, null), [
                new EngineVersion { EngineName = "3.6 V6 293 KM", PowerHP = 293, PowerKW = 215, Displacement = 3604, FuelTypeId = ben,
                    TorqueNm = 353, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 8.6m, TopSpeedKmh = 192, FuelConsumptionCombined = 12.2m },
                new EngineVersion { EngineName = "4xe PHEV 380 KM", PowerHP = 380, PowerKW = 279, Displacement = 1995, FuelTypeId = phev,
                    TorqueNm = 637, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.0m, TopSpeedKmh = 210, FuelConsumptionCombined = 3.4m },
                new EngineVersion { EngineName = "3.0 CRD V6 264 KM", PowerHP = 264, PowerKW = 194, Displacement = 2987, FuelTypeId = die,
                    TorqueNm = 600, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 8.3m, TopSpeedKmh = 208, FuelConsumptionCombined = 8.0m },
            ]);

            int cherokee = GetOrCreateModel(jeepId, "Cherokee", "jeep-cherokee");
            AddOrReplaceEngines(GetOrFixGeneration(cherokee, "KL (2013–2020)", "jeep-cherokee-kl", 2013, 2020), 130, [
                new EngineVersion { EngineName = "2.0 MultiJet 170 KM", PowerHP = 170, PowerKW = 125, Displacement = 1956, FuelTypeId = die,
                    TorqueNm = 350, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 10.0m, TopSpeedKmh = 195, FuelConsumptionCombined = 6.4m },
                new EngineVersion { EngineName = "2.4 MultiAir 175 KM", PowerHP = 175, PowerKW = 129, Displacement = 2360, FuelTypeId = ben,
                    TorqueNm = 224, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 10.4m, TopSpeedKmh = 187, FuelConsumptionCombined = 8.8m },
            ]);

            int gladiator = GetOrCreateModel(jeepId, "Gladiator", "jeep-gladiator");
            AddOrReplaceEngines(GetOrFixGeneration(gladiator, "JT (2019–)", "jeep-gladiator-jt", 2019, null), 250, [
                new EngineVersion { EngineName = "3.6 V6 Pentastar 285 KM", PowerHP = 285, PowerKW = 209, Displacement = 3604, FuelTypeId = ben,
                    TorqueNm = 353, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 8.8m, TopSpeedKmh = 152, FuelConsumptionCombined = 13.0m },
            ]);

            int avenger = GetOrCreateModel(jeepId, "Avenger", "jeep-avenger");
            AddOrReplaceEngines(GetOrFixGeneration(avenger, "I (2023–)", "jeep-avenger-i", 2023, null), 100, [
                new EngineVersion { EngineName = "1.2 T4 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1199, FuelTypeId = mild,
                    TorqueNm = 205, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.7m, TopSpeedKmh = 165, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "Electric 156 KM", PowerHP = 156, PowerKW = 115, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 260, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 0, Acceleration0100 = 9.0m, TopSpeedKmh = 150, FuelConsumptionCombined = 16.0m },
            ]);
        }

        // ── Mazda ─────────────────────────────────────────────────────────────────
        {
            int mazId = GetOrCreateBrand("Mazda", "mazda", "osobowe");
            int m6 = GetOrCreateModel(mazId, "Mazda6", "mazda6");
            AddEngines(GetOrCreateGeneration(m6, "GL (2012–)", "mazda6-gl", 2012, null), [
                new EngineVersion { EngineName = "2.0 SKYACTIV-G 145 KM", PowerHP = 145, PowerKW = 107, Displacement = 1997, FuelTypeId = ben,
                    TorqueNm = 210, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.4m, TopSpeedKmh = 205, FuelConsumptionCombined = 6.6m },
                new EngineVersion { EngineName = "2.5 SKYACTIV-G 194 KM", PowerHP = 194, PowerKW = 143, Displacement = 2488, FuelTypeId = ben,
                    TorqueNm = 258, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.2m, TopSpeedKmh = 215, FuelConsumptionCombined = 7.9m },
                new EngineVersion { EngineName = "2.2 SKYACTIV-D 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 2191, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.8m, TopSpeedKmh = 215, FuelConsumptionCombined = 4.7m },
                new EngineVersion { EngineName = "2.2 SKYACTIV-D 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 2191, FuelTypeId = die,
                    TorqueNm = 450, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.1m, TopSpeedKmh = 222, FuelConsumptionCombined = 4.8m },
            ]);
            int cx30 = GetOrCreateModel(mazId, "CX-30", "mazda-cx30");
            AddEngines(GetOrCreateGeneration(cx30, "DM (2019–)", "mazda-cx30-dm", 2019, null), [
                new EngineVersion { EngineName = "2.0 SKYACTIV-G 122 KM", PowerHP = 122, PowerKW = 90, Displacement = 1997, FuelTypeId = ben,
                    TorqueNm = 213, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.9m, TopSpeedKmh = 196, FuelConsumptionCombined = 6.6m },
                new EngineVersion { EngineName = "2.0 e-SKYACTIV X 186 KM MHEV", PowerHP = 186, PowerKW = 137, Displacement = 1997, FuelTypeId = mild,
                    TorqueNm = 240, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.7m, TopSpeedKmh = 218, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "2.5 e-SKYACTIV PHEV 327 KM", PowerHP = 327, PowerKW = 241, Displacement = 2488, FuelTypeId = phev,
                    TorqueNm = 150, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 5.8m, TopSpeedKmh = 200, FuelConsumptionCombined = 1.5m },
                new EngineVersion { EngineName = "1.8 SKYACTIV-D 116 KM", PowerHP = 116, PowerKW = 85, Displacement = 1756, FuelTypeId = die,
                    TorqueNm = 270, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.4m, TopSpeedKmh = 190, FuelConsumptionCombined = 4.5m },
            ]);
            int mx5 = GetOrCreateModel(mazId, "MX-5", "mazda-mx5");
            AddEngines(GetOrCreateGeneration(mx5, "ND (2015–)", "mazda-mx5-nd", 2015, null), [
                new EngineVersion { EngineName = "1.5 SKYACTIV-G 132 KM", PowerHP = 132, PowerKW = 97, Displacement = 1496, FuelTypeId = ben,
                    TorqueNm = 152, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 8.3m, TopSpeedKmh = 200, FuelConsumptionCombined = 6.8m },
                new EngineVersion { EngineName = "2.0 SKYACTIV-G 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 1997, FuelTypeId = ben,
                    TorqueNm = 205, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 6.5m, TopSpeedKmh = 218, FuelConsumptionCombined = 7.4m },
            ]);

            int mazda2 = GetOrCreateModel(mazId, "Mazda2", "mazda-2");
            AddOrReplaceEngines(GetOrFixGeneration(mazda2, "DJ (2014–)", "mazda2-dj", 2014, null), 70, [
                new EngineVersion { EngineName = "1.5 Skyactiv-G 75 KM", PowerHP = 75, PowerKW = 55, Displacement = 1496, FuelTypeId = ben,
                    TorqueNm = 135, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.7m, TopSpeedKmh = 165, FuelConsumptionCombined = 4.9m },
                new EngineVersion { EngineName = "1.5 Skyactiv-G 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1496, FuelTypeId = ben,
                    TorqueNm = 148, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.4m, TopSpeedKmh = 175, FuelConsumptionCombined = 5.0m },
            ]);

            int cx3 = GetOrCreateModel(mazId, "CX-3", "mazda-cx3");
            AddOrReplaceEngines(GetOrFixGeneration(cx3, "DK (2015–)", "mazda-cx3-dk", 2015, null), 90, [
                new EngineVersion { EngineName = "2.0 Skyactiv-G 121 KM", PowerHP = 121, PowerKW = 89, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 205, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.0m, TopSpeedKmh = 192, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "1.5 Skyactiv-D 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 270, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.9m, TopSpeedKmh = 180, FuelConsumptionCombined = 4.5m },
            ]);

            int cx60 = GetOrCreateModel(mazId, "CX-60", "mazda-cx60");
            AddOrReplaceEngines(GetOrFixGeneration(cx60, "KH (2022–)", "mazda-cx60-kh", 2022, null), 190, [
                new EngineVersion { EngineName = "3.3 e-Skyactiv D 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 3283, FuelTypeId = mild,
                    TorqueNm = 450, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 8.4m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "2.5 e-Skyactiv PHEV 327 KM", PowerHP = 327, PowerKW = 241, Displacement = 2488, FuelTypeId = phev,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 5.8m, TopSpeedKmh = 200, FuelConsumptionCombined = 1.5m },
            ]);
        }

        // ── Peugeot (remaining) ───────────────────────────────────────────────────
        {
            int peugId = GetOrCreateBrand("Peugeot", "peugeot", "osobowe", "dostawcze");
            int p208 = GetOrCreateModel(peugId, "208", "peugeot-208");
            AddEngines(GetOrCreateGeneration(p208, "I (2012–2019)", "peugeot-208-i", 2012, 2019), [
                new EngineVersion { EngineName = "1.2 PureTech 82 KM", PowerHP = 82, PowerKW = 60, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 118, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 14.0m, TopSpeedKmh = 167, FuelConsumptionCombined = 4.9m },
                new EngineVersion { EngineName = "1.6 VTi 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 160, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.9m, TopSpeedKmh = 201, FuelConsumptionCombined = 5.7m },
                new EngineVersion { EngineName = "1.6 THP GTi 208 KM", PowerHP = 208, PowerKW = 153, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 300, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.5m, TopSpeedKmh = 235, FuelConsumptionCombined = 7.3m },
                new EngineVersion { EngineName = "1.4 HDi 70 KM", PowerHP = 70, PowerKW = 51, Displacement = 1398, FuelTypeId = die,
                    TorqueNm = 170, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 15.9m, TopSpeedKmh = 158, FuelConsumptionCombined = 3.8m },
                new EngineVersion { EngineName = "1.6 BlueHDi 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1560, FuelTypeId = die,
                    TorqueNm = 254, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 185, FuelConsumptionCombined = 3.8m },
            ]);
            int p2008 = GetOrCreateModel(peugId, "2008", "peugeot-2008");
            AddEngines(GetOrCreateGeneration(p2008, "II (2019–)", "peugeot-2008-ii", 2019, null), [
                new EngineVersion { EngineName = "1.2 PureTech 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 205, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.5m, TopSpeedKmh = 183, FuelConsumptionCombined = 5.7m },
                new EngineVersion { EngineName = "1.2 PureTech 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.5m, TopSpeedKmh = 200, FuelConsumptionCombined = 5.8m },
                new EngineVersion { EngineName = "e-2008 EV 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 260, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 0, Acceleration0100 = 9.1m, TopSpeedKmh = 150, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "1.5 BlueHDi 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 185, FuelConsumptionCombined = 4.4m },
            ]);
            int p3008 = GetOrCreateModel(peugId, "3008", "peugeot-3008");
            AddEngines(GetOrCreateGeneration(p3008, "III (2023–)", "peugeot-3008-iii", 2023, null), [
                new EngineVersion { EngineName = "1.2 PureTech 136 KM MHEV", PowerHP = 136, PowerKW = 100, Displacement = 1199, FuelTypeId = mild,
                    TorqueNm = 230, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.3m, TopSpeedKmh = 200, FuelConsumptionCombined = 5.7m },
                new EngineVersion { EngineName = "e-3008 EV 213 KM FWD", PowerHP = 213, PowerKW = 157, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 345, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 0, Acceleration0100 = 8.8m, TopSpeedKmh = 170, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "e-3008 EV 320 KM AWD", PowerHP = 320, PowerKW = 235, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 6.3m, TopSpeedKmh = 190, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "1.6 PHEV 195 KM FWD", PowerHP = 195, PowerKW = 143, Displacement = 1598, FuelTypeId = phev,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.0m, TopSpeedKmh = 200, FuelConsumptionCombined = 1.7m },
            ]);
            int boxer = GetOrCreateModel(peugId, "Boxer", "peugeot-boxer");
            AddEngines(GetOrCreateGeneration(boxer, "III (2006–)", "peugeot-boxer-iii", 2006, null), [
                new EngineVersion { EngineName = "2.2 BlueHDi 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 2198, FuelTypeId = die,
                    TorqueNm = 340, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = 155, FuelConsumptionCombined = 8.1m },
                new EngineVersion { EngineName = "2.2 BlueHDi 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 2198, FuelTypeId = die,
                    TorqueNm = 370, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = 160, FuelConsumptionCombined = 8.3m },
                new EngineVersion { EngineName = "3.0 BlueHDi 163 KM", PowerHP = 163, PowerKW = 120, Displacement = 2999, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = 165, FuelConsumptionCombined = 9.0m },
                new EngineVersion { EngineName = "e-Boxer EV 122 KM", PowerHP = 122, PowerKW = 90, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 260, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 0, Acceleration0100 = null, TopSpeedKmh = 120, FuelConsumptionCombined = null },
            ]);

            int p508 = GetOrCreateModel(peugId, "508", "peugeot-508");
            AddOrReplaceEngines(GetOrFixGeneration(p508, "II (2018–)", "peugeot-508-ii", 2018, null), 130, [
                new EngineVersion { EngineName = "1.6 PureTech 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.3m, TopSpeedKmh = 225, FuelConsumptionCombined = 6.1m },
                new EngineVersion { EngineName = "PSE Hybrid4 360 KM AWD", PowerHP = 360, PowerKW = 265, Displacement = 1598, FuelTypeId = phev,
                    TorqueNm = 520, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 5.2m, TopSpeedKmh = 250, FuelConsumptionCombined = 1.9m },
                new EngineVersion { EngineName = "2.0 BlueHDi 160 KM", PowerHP = 160, PowerKW = 118, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.9m, TopSpeedKmh = 226, FuelConsumptionCombined = 4.8m },
            ]);

            int p5008 = GetOrCreateModel(peugId, "5008", "peugeot-5008");
            AddOrReplaceEngines(GetOrFixGeneration(p5008, "II (2016–)", "peugeot-5008-ii", 2016, null), 130, [
                new EngineVersion { EngineName = "1.2 PureTech 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.9m, TopSpeedKmh = 188, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "1.6 PureTech 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.9m, TopSpeedKmh = 213, FuelConsumptionCombined = 6.8m },
                new EngineVersion { EngineName = "2.0 BlueHDi 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1997, FuelTypeId = die,
                    TorqueNm = 370, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 202, FuelConsumptionCombined = 5.2m },
            ]);

            int partner = GetOrCreateModel(peugId, "Partner", "peugeot-partner");
            AddOrReplaceEngines(GetOrFixGeneration(partner, "III (2018–)", "peugeot-partner-iii", 2018, null), 90, [
                new EngineVersion { EngineName = "1.2 PureTech 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 205, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.5m, TopSpeedKmh = 183, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "1.5 BlueHDi 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1499, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.4m, TopSpeedKmh = 168, FuelConsumptionCombined = 4.7m },
                new EngineVersion { EngineName = "e-Partner EV 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 260, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 0, Acceleration0100 = 11.1m, TopSpeedKmh = 135, FuelConsumptionCombined = 16.7m },
            ]);
        }

        // ── Yamaha Motorcycles ────────────────────────────────────────────────────
        {
            int yamId = GetOrCreateBrand("Yamaha", "yamaha", "motocykle");
            int mt07 = GetOrCreateModel(yamId, "MT-07", "yamaha-mt07");
            AddEngines(GetOrCreateGeneration(mt07, "RM04 (2013–2020)", "yamaha-mt07-rm04", 2013, 2020), [
                new EngineVersion { EngineName = "689cc CP2 75 KM", PowerHP = 75, PowerKW = 55, Displacement = 689, FuelTypeId = ben,
                    TorqueNm = 68, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 3.8m, TopSpeedKmh = 180, FuelConsumptionCombined = 4.8m },
            ]);
            AddEngines(GetOrCreateGeneration(mt07, "RM36 (2021–)", "yamaha-mt07-rm36", 2021, null), [
                new EngineVersion { EngineName = "689cc CP2 73 KM Euro 5", PowerHP = 73, PowerKW = 54, Displacement = 689, FuelTypeId = ben,
                    TorqueNm = 67, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 3.8m, TopSpeedKmh = 180, FuelConsumptionCombined = 4.8m },
            ]);
            int mt09 = GetOrCreateModel(yamId, "MT-09", "yamaha-mt09");
            AddEngines(GetOrCreateGeneration(mt09, "RN29 (2013–2020)", "yamaha-mt09-rn29", 2013, 2020), [
                new EngineVersion { EngineName = "847cc CP3 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 847, FuelTypeId = ben,
                    TorqueNm = 87, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, Acceleration0100 = 3.4m, TopSpeedKmh = 200, FuelConsumptionCombined = 5.5m },
            ]);
            AddEngines(GetOrCreateGeneration(mt09, "RN57 (2021–)", "yamaha-mt09-rn57", 2021, null), [
                new EngineVersion { EngineName = "890cc CP3 119 KM Euro 5", PowerHP = 119, PowerKW = 88, Displacement = 890, FuelTypeId = ben,
                    TorqueNm = 93, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, Acceleration0100 = 3.2m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.6m },
            ]);
            int tracer9 = GetOrCreateModel(yamId, "Tracer 9", "yamaha-tracer9");
            AddEngines(GetOrCreateGeneration(tracer9, "RN57 GT (2021–)", "yamaha-tracer9-rn57", 2021, null), [
                new EngineVersion { EngineName = "890cc CP3 119 KM Euro 5", PowerHP = 119, PowerKW = 88, Displacement = 890, FuelTypeId = ben,
                    TorqueNm = 93, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, Acceleration0100 = 3.2m, TopSpeedKmh = 215, FuelConsumptionCombined = 5.7m },
            ]);
            int r1 = GetOrCreateModel(yamId, "YZF-R1", "yamaha-yzf-r1");
            AddEngines(GetOrCreateGeneration(r1, "RN32 (2015–)", "yamaha-r1-rn32", 2015, null), [
                new EngineVersion { EngineName = "998cc Crossplane 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 998, FuelTypeId = ben,
                    TorqueNm = 112, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 3.0m, TopSpeedKmh = 299, FuelConsumptionCombined = 7.3m },
            ]);
            int tmax = GetOrCreateModel(yamId, "TMAX 560", "yamaha-tmax560");
            AddEngines(GetOrCreateGeneration(tmax, "SJ19 (2020–)", "yamaha-tmax560-sj19", 2020, null), [
                new EngineVersion { EngineName = "562cc parallel-twin 47.6 KM", PowerHP = 47, PowerKW = 35, Displacement = 562, FuelTypeId = ben,
                    TorqueNm = 55, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = null, TopSpeedKmh = 160, FuelConsumptionCombined = 4.6m },
            ]);
        }

        // ── Kawasaki Motorcycles ──────────────────────────────────────────────────
        {
            int kawId = GetOrCreateBrand("Kawasaki", "kawasaki", "motocykle");
            int z900 = GetOrCreateModel(kawId, "Z900", "kawasaki-z900");
            AddEngines(GetOrCreateGeneration(z900, "ZR900 (2017–)", "kawasaki-z900-zr900", 2017, null), [
                new EngineVersion { EngineName = "948cc inline-4 125 KM", PowerHP = 125, PowerKW = 92, Displacement = 948, FuelTypeId = ben,
                    TorqueNm = 98, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 3.2m, TopSpeedKmh = 235, FuelConsumptionCombined = 5.8m },
            ]);
            int ninja650 = GetOrCreateModel(kawId, "Ninja 650", "kawasaki-ninja650");
            AddEngines(GetOrCreateGeneration(ninja650, "ER-6 (2017–)", "kawasaki-ninja650-er6", 2017, null), [
                new EngineVersion { EngineName = "649cc parallel-twin 68 KM", PowerHP = 68, PowerKW = 50, Displacement = 649, FuelTypeId = ben,
                    TorqueNm = 65, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 4.2m, TopSpeedKmh = 195, FuelConsumptionCombined = 4.6m },
            ]);
            int zx10r = GetOrCreateModel(kawId, "ZX-10R", "kawasaki-zx10r");
            AddEngines(GetOrCreateGeneration(zx10r, "2021– (ZX1002L)", "kawasaki-zx10r-2021", 2021, null), [
                new EngineVersion { EngineName = "998cc inline-4 203 KM", PowerHP = 203, PowerKW = 149, Displacement = 998, FuelTypeId = ben,
                    TorqueNm = 114, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 2.8m, TopSpeedKmh = 299, FuelConsumptionCombined = 7.5m },
            ]);
            int versys = GetOrCreateModel(kawId, "Versys 650", "kawasaki-versys650");
            AddEngines(GetOrCreateGeneration(versys, "LE650 (2015–)", "kawasaki-versys650-le650", 2015, null), [
                new EngineVersion { EngineName = "649cc parallel-twin 69 KM", PowerHP = 69, PowerKW = 51, Displacement = 649, FuelTypeId = ben,
                    TorqueNm = 64, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 4.5m, TopSpeedKmh = 195, FuelConsumptionCombined = 4.9m },
            ]);
        }

        // ── Ducati Motorcycles ────────────────────────────────────────────────────
        {
            int ducId = GetOrCreateBrand("Ducati", "ducati", "motocykle");
            int panigale = GetOrCreateModel(ducId, "Panigale V4", "ducati-panigale-v4");
            AddEngines(GetOrCreateGeneration(panigale, "2018–", "ducati-panigale-v4-2018", 2018, null), [
                new EngineVersion { EngineName = "1103cc Desmosedici V4 214 KM", PowerHP = 214, PowerKW = 157, Displacement = 1103, FuelTypeId = ben,
                    TorqueNm = 124, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 2.9m, TopSpeedKmh = 306, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "1103cc V4 S 215 KM", PowerHP = 215, PowerKW = 158, Displacement = 1103, FuelTypeId = ben,
                    TorqueNm = 124, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 2.8m, TopSpeedKmh = 310, FuelConsumptionCombined = 7.5m },
            ]);
            int monster = GetOrCreateModel(ducId, "Monster", "ducati-monster");
            AddEngines(GetOrCreateGeneration(monster, "937 (2021–)", "ducati-monster-937", 2021, null), [
                new EngineVersion { EngineName = "937cc Testastretta V-Twin 111 KM", PowerHP = 111, PowerKW = 82, Displacement = 937, FuelTypeId = ben,
                    TorqueNm = 93, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 3.5m, TopSpeedKmh = 230, FuelConsumptionCombined = 6.3m },
            ]);
            int multi = GetOrCreateModel(ducId, "Multistrada V4", "ducati-multistrada-v4");
            AddEngines(GetOrCreateGeneration(multi, "2021–", "ducati-multistrada-v4-2021", 2021, null), [
                new EngineVersion { EngineName = "1158cc Granturismo V4 170 KM", PowerHP = 170, PowerKW = 125, Displacement = 1158, FuelTypeId = ben,
                    TorqueNm = 125, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 3.0m, TopSpeedKmh = 260, FuelConsumptionCombined = 6.8m },
            ]);
            int scrambler = GetOrCreateModel(ducId, "Scrambler 803", "ducati-scrambler803");
            AddEngines(GetOrCreateGeneration(scrambler, "2015–", "ducati-scrambler803-2015", 2015, null), [
                new EngineVersion { EngineName = "803cc Desmodue L-Twin 73 KM", PowerHP = 73, PowerKW = 54, Displacement = 803, FuelTypeId = ben,
                    TorqueNm = 67, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 4.8m, TopSpeedKmh = 180, FuelConsumptionCombined = 5.0m },
            ]);
        }

        // ── Triumph Motorcycles ───────────────────────────────────────────────────
        {
            int triId = GetOrCreateBrand("Triumph", "triumph", "motocykle");
            int bonnie = GetOrCreateModel(triId, "Bonneville T120", "triumph-bonneville-t120");
            AddEngines(GetOrCreateGeneration(bonnie, "2016–", "triumph-bonnie-t120-2016", 2016, null), [
                new EngineVersion { EngineName = "1200cc High Torque T120 79 KM", PowerHP = 79, PowerKW = 58, Displacement = 1200, FuelTypeId = ben,
                    TorqueNm = 105, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 4.2m, TopSpeedKmh = 185, FuelConsumptionCombined = 4.8m },
            ]);
            int st = GetOrCreateModel(triId, "Street Triple 765", "triumph-street-triple-765");
            AddEngines(GetOrCreateGeneration(st, "2017–", "triumph-street-triple-2017", 2017, null), [
                new EngineVersion { EngineName = "765cc Triple 118 KM R", PowerHP = 118, PowerKW = 87, Displacement = 765, FuelTypeId = ben,
                    TorqueNm = 77, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, Acceleration0100 = 3.4m, TopSpeedKmh = 220, FuelConsumptionCombined = 5.2m },
                new EngineVersion { EngineName = "765cc Triple RS 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 765, FuelTypeId = ben,
                    TorqueNm = 80, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, Acceleration0100 = 3.2m, TopSpeedKmh = 240, FuelConsumptionCombined = 5.4m },
            ]);
            int tiger = GetOrCreateModel(triId, "Tiger 900", "triumph-tiger-900");
            AddEngines(GetOrCreateGeneration(tiger, "2020–", "triumph-tiger900-2020", 2020, null), [
                new EngineVersion { EngineName = "888cc Triple 95 KM GT", PowerHP = 95, PowerKW = 70, Displacement = 888, FuelTypeId = ben,
                    TorqueNm = 87, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, Acceleration0100 = 3.5m, TopSpeedKmh = 215, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "888cc Triple Rally Pro 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 888, FuelTypeId = ben,
                    TorqueNm = 87, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, Acceleration0100 = 3.4m, TopSpeedKmh = 220, FuelConsumptionCombined = 5.6m },
            ]);
        }

        // ── Harley-Davidson Motorcycles ───────────────────────────────────────────
        {
            int hdId = GetOrCreateBrand("Harley-Davidson", "harley-davidson", "motocykle");
            int sps = GetOrCreateModel(hdId, "Sportster S", "hd-sportster-s");
            AddEngines(GetOrCreateGeneration(sps, "RH1250S (2021–)", "hd-sportster-s-2021", 2021, null), [
                new EngineVersion { EngineName = "1252cc Revolution Max 121 KM", PowerHP = 121, PowerKW = 89, Displacement = 1252, FuelTypeId = ben,
                    TorqueNm = 127, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 3.9m, TopSpeedKmh = 200, FuelConsumptionCombined = 5.8m },
            ]);
            int fatbob = GetOrCreateModel(hdId, "Fat Bob", "hd-fatbob");
            AddEngines(GetOrCreateGeneration(fatbob, "FXFBS (2017–)", "hd-fatbob-fxfbs", 2017, null), [
                new EngineVersion { EngineName = "1868cc Milwaukee-Eight 107 KM", PowerHP = 107, PowerKW = 79, Displacement = 1868, FuelTypeId = ben,
                    TorqueNm = 155, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 4.5m, TopSpeedKmh = 185, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "1923cc Milwaukee-Eight 114 KM", PowerHP = 114, PowerKW = 84, Displacement = 1923, FuelTypeId = ben,
                    TorqueNm = 162, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 4.3m, TopSpeedKmh = 190, FuelConsumptionCombined = 6.8m },
            ]);
            int rg = GetOrCreateModel(hdId, "Road Glide", "hd-road-glide");
            AddEngines(GetOrCreateGeneration(rg, "2017–", "hd-road-glide-2017", 2017, null), [
                new EngineVersion { EngineName = "1868cc Milwaukee-Eight 107 KM", PowerHP = 107, PowerKW = 79, Displacement = 1868, FuelTypeId = ben,
                    TorqueNm = 155, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 5.0m, TopSpeedKmh = 185, FuelConsumptionCombined = 6.8m },
                new EngineVersion { EngineName = "1923cc Milwaukee-Eight 114 KM", PowerHP = 114, PowerKW = 84, Displacement = 1923, FuelTypeId = ben,
                    TorqueNm = 162, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 4.8m, TopSpeedKmh = 190, FuelConsumptionCombined = 7.0m },
            ]);
        }

        // ── KTM Motorcycles ───────────────────────────────────────────────────────
        {
            int ktmId = GetOrCreateBrand("KTM", "ktm", "motocykle");
            int d390 = GetOrCreateModel(ktmId, "390 Duke", "ktm-390-duke");
            AddEngines(GetOrCreateGeneration(d390, "2017–", "ktm-390duke-2017", 2017, null), [
                new EngineVersion { EngineName = "373cc single 44 KM", PowerHP = 44, PowerKW = 32, Displacement = 373, FuelTypeId = ben,
                    TorqueNm = 37, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 1, Acceleration0100 = 4.8m, TopSpeedKmh = 167, FuelConsumptionCombined = 3.6m },
            ]);
            int d790 = GetOrCreateModel(ktmId, "790 Duke", "ktm-790-duke");
            AddEngines(GetOrCreateGeneration(d790, "2018–", "ktm-790duke-2018", 2018, null), [
                new EngineVersion { EngineName = "799cc LC8c parallel-twin 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 799, FuelTypeId = ben,
                    TorqueNm = 87, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 3.5m, TopSpeedKmh = 220, FuelConsumptionCombined = 5.7m },
            ]);
            int sdr = GetOrCreateModel(ktmId, "1290 Super Duke R", "ktm-1290-super-duke-r");
            AddEngines(GetOrCreateGeneration(sdr, "2020–", "ktm-1290sdr-2020", 2020, null), [
                new EngineVersion { EngineName = "1301cc LC8 V-Twin 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 1301, FuelTypeId = ben,
                    TorqueNm = 140, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 2.9m, TopSpeedKmh = 282, FuelConsumptionCombined = 7.2m },
            ]);
        }

        // ── Aprilia Motorcycles ───────────────────────────────────────────────────
        {
            int aprId = GetOrCreateBrand("Aprilia", "aprilia", "motocykle");
            int rs660 = GetOrCreateModel(aprId, "RS 660", "aprilia-rs660");
            AddEngines(GetOrCreateGeneration(rs660, "2021–", "aprilia-rs660-2021", 2021, null), [
                new EngineVersion { EngineName = "659cc parallel-twin 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 659, FuelTypeId = ben,
                    TorqueNm = 67, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 3.7m, TopSpeedKmh = 240, FuelConsumptionCombined = 5.0m },
            ]);
            int tuono660 = GetOrCreateModel(aprId, "Tuono 660", "aprilia-tuono660");
            AddEngines(GetOrCreateGeneration(tuono660, "2021–", "aprilia-tuono660-2021", 2021, null), [
                new EngineVersion { EngineName = "659cc parallel-twin 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 659, FuelTypeId = ben,
                    TorqueNm = 67, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 3.8m, TopSpeedKmh = 230, FuelConsumptionCombined = 5.0m },
            ]);
            int rsv4 = GetOrCreateModel(aprId, "RSV4", "aprilia-rsv4");
            AddEngines(GetOrCreateGeneration(rsv4, "2021–", "aprilia-rsv4-2021", 2021, null), [
                new EngineVersion { EngineName = "1099cc V4 217 KM", PowerHP = 217, PowerKW = 160, Displacement = 1099, FuelTypeId = ben,
                    TorqueNm = 125, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 2.9m, TopSpeedKmh = 305, FuelConsumptionCombined = 7.4m },
            ]);
            int tuonov4 = GetOrCreateModel(aprId, "Tuono V4", "aprilia-tuono-v4");
            AddEngines(GetOrCreateGeneration(tuonov4, "2021–", "aprilia-tuono-v4-2021", 2021, null), [
                new EngineVersion { EngineName = "1099cc V4 175 KM", PowerHP = 175, PowerKW = 129, Displacement = 1099, FuelTypeId = ben,
                    TorqueNm = 121, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 3.0m, TopSpeedKmh = 280, FuelConsumptionCombined = 7.2m },
            ]);
        }

        // ── MV Agusta Motorcycles ─────────────────────────────────────────────────
        {
            int mvId = GetOrCreateBrand("MV Agusta", "mv-agusta", "motocykle");
            int b800 = GetOrCreateModel(mvId, "Brutale 800", "mv-agusta-brutale800");
            AddEngines(GetOrCreateGeneration(b800, "2012–", "mv-brutale800-2012", 2012, null), [
                new EngineVersion { EngineName = "798cc triple 140 KM RR", PowerHP = 140, PowerKW = 103, Displacement = 798, FuelTypeId = ben,
                    TorqueNm = 87, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, Acceleration0100 = 3.5m, TopSpeedKmh = 240, FuelConsumptionCombined = 6.3m },
            ]);
            int f3800 = GetOrCreateModel(mvId, "F3 800", "mv-agusta-f3-800");
            AddEngines(GetOrCreateGeneration(f3800, "2013–", "mv-f3-800-2013", 2013, null), [
                new EngineVersion { EngineName = "798cc triple 148 KM RC", PowerHP = 148, PowerKW = 109, Displacement = 798, FuelTypeId = ben,
                    TorqueNm = 88, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, Acceleration0100 = 3.2m, TopSpeedKmh = 268, FuelConsumptionCombined = 6.2m },
            ]);
            int tv = GetOrCreateModel(mvId, "Turismo Veloce", "mv-agusta-turismo-veloce");
            AddEngines(GetOrCreateGeneration(tv, "2014–", "mv-turismo-veloce-2014", 2014, null), [
                new EngineVersion { EngineName = "798cc triple 110 KM 800", PowerHP = 110, PowerKW = 81, Displacement = 798, FuelTypeId = ben,
                    TorqueNm = 81, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, Acceleration0100 = 3.8m, TopSpeedKmh = 225, FuelConsumptionCombined = 5.8m },
            ]);
        }

        // ── Royal Enfield Motorcycles ─────────────────────────────────────────────
        {
            int reId = GetOrCreateBrand("Royal Enfield", "royal-enfield", "motocykle");
            int meteor = GetOrCreateModel(reId, "Meteor 350", "re-meteor350");
            AddEngines(GetOrCreateGeneration(meteor, "2020–", "re-meteor350-2020", 2020, null), [
                new EngineVersion { EngineName = "349cc single 20 KM", PowerHP = 20, PowerKW = 15, Displacement = 349, FuelTypeId = ben,
                    TorqueNm = 27, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 1, Acceleration0100 = null, TopSpeedKmh = 130, FuelConsumptionCombined = 3.2m },
            ]);
            int himalayan = GetOrCreateModel(reId, "Himalayan", "re-himalayan");
            AddEngines(GetOrCreateGeneration(himalayan, "2016–2023 (411cc)", "re-himalayan-411", 2016, 2023), [
                new EngineVersion { EngineName = "411cc single 24.5 KM", PowerHP = 25, PowerKW = 18, Displacement = 411, FuelTypeId = ben,
                    TorqueNm = 32, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 1, Acceleration0100 = null, TopSpeedKmh = 125, FuelConsumptionCombined = 3.3m },
            ]);
            AddEngines(GetOrCreateGeneration(himalayan, "2024– (450cc)", "re-himalayan-450", 2024, null), [
                new EngineVersion { EngineName = "452cc single 40 KM", PowerHP = 40, PowerKW = 29, Displacement = 452, FuelTypeId = ben,
                    TorqueNm = 40, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 1, Acceleration0100 = null, TopSpeedKmh = 155, FuelConsumptionCombined = 3.5m },
            ]);
            int classic350 = GetOrCreateModel(reId, "Classic 350", "re-classic350");
            AddEngines(GetOrCreateGeneration(classic350, "2021–", "re-classic350-2021", 2021, null), [
                new EngineVersion { EngineName = "349cc single 20 KM", PowerHP = 20, PowerKW = 15, Displacement = 349, FuelTypeId = ben,
                    TorqueNm = 27, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 1, Acceleration0100 = null, TopSpeedKmh = 130, FuelConsumptionCombined = 3.1m },
            ]);
        }

        // ── Indian Motorcycles ────────────────────────────────────────────────────
        {
            int indId = GetOrCreateBrand("Indian", "indian", "motocykle");
            int scout = GetOrCreateModel(indId, "Scout", "indian-scout");
            AddEngines(GetOrCreateGeneration(scout, "2015–", "indian-scout-2015", 2015, null), [
                new EngineVersion { EngineName = "1133cc V-Twin 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1133, FuelTypeId = ben,
                    TorqueNm = 98, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 4.5m, TopSpeedKmh = 193, FuelConsumptionCombined = 6.3m },
            ]);
            int chief = GetOrCreateModel(indId, "Chief", "indian-chief");
            AddEngines(GetOrCreateGeneration(chief, "2021–", "indian-chief-2021", 2021, null), [
                new EngineVersion { EngineName = "1890cc Thunderstroke 116 KM", PowerHP = 116, PowerKW = 85, Displacement = 1890, FuelTypeId = ben,
                    TorqueNm = 166, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 4.5m, TopSpeedKmh = 185, FuelConsumptionCombined = 6.9m },
            ]);
            int challenger = GetOrCreateModel(indId, "Challenger", "indian-challenger");
            AddEngines(GetOrCreateGeneration(challenger, "2020–", "indian-challenger-2020", 2020, null), [
                new EngineVersion { EngineName = "1770cc PowerPlus 122 KM", PowerHP = 122, PowerKW = 90, Displacement = 1770, FuelTypeId = ben,
                    TorqueNm = 178, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 4.2m, TopSpeedKmh = 190, FuelConsumptionCombined = 7.5m },
            ]);
        }

        // ── Husqvarna Motorcycles ─────────────────────────────────────────────────
        {
            int husqId = GetOrCreateBrand("Husqvarna", "husqvarna", "motocykle");
            int sp401 = GetOrCreateModel(husqId, "Svartpilen 401", "husqvarna-svartpilen401");
            AddEngines(GetOrCreateGeneration(sp401, "2017–", "husqvarna-svartpilen401-2017", 2017, null), [
                new EngineVersion { EngineName = "373cc single 44 KM", PowerHP = 44, PowerKW = 32, Displacement = 373, FuelTypeId = ben,
                    TorqueNm = 37, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 1, Acceleration0100 = 4.8m, TopSpeedKmh = 155, FuelConsumptionCombined = 3.6m },
            ]);
            int vp401 = GetOrCreateModel(husqId, "Vitpilen 401", "husqvarna-vitpilen401");
            AddEngines(GetOrCreateGeneration(vp401, "2018–", "husqvarna-vitpilen401-2018", 2018, null), [
                new EngineVersion { EngineName = "373cc single 44 KM", PowerHP = 44, PowerKW = 32, Displacement = 373, FuelTypeId = ben,
                    TorqueNm = 37, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 1, Acceleration0100 = 4.8m, TopSpeedKmh = 160, FuelConsumptionCombined = 3.6m },
            ]);
            int norden = GetOrCreateModel(husqId, "Norden 901", "husqvarna-norden901");
            AddEngines(GetOrCreateGeneration(norden, "2021–", "husqvarna-norden901-2021", 2021, null), [
                new EngineVersion { EngineName = "889cc parallel-twin 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 889, FuelTypeId = ben,
                    TorqueNm = 100, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 3.6m, TopSpeedKmh = 215, FuelConsumptionCombined = 5.5m },
            ]);
        }

        // ── BMW Motorcycles ───────────────────────────────────────────────────────
        {
            int bmwMotoId = GetOrCreateBrand("BMW Motorrad", "bmw-motorrad", "motocykle");
            int gs = GetOrCreateModel(bmwMotoId, "R 1250 GS", "bmw-r1250gs");
            AddEngines(GetOrCreateGeneration(gs, "2018–", "bmw-r1250gs-2018", 2018, null), [
                new EngineVersion { EngineName = "1254cc ShiftCam Boxer 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1254, FuelTypeId = ben,
                    TorqueNm = 143, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 3.5m, TopSpeedKmh = 220, FuelConsumptionCombined = 5.8m },
            ]);
            int s1000rr = GetOrCreateModel(bmwMotoId, "S 1000 RR", "bmw-s1000rr");
            AddEngines(GetOrCreateGeneration(s1000rr, "2019–", "bmw-s1000rr-2019", 2019, null), [
                new EngineVersion { EngineName = "999cc ShiftCam inline-4 210 KM", PowerHP = 210, PowerKW = 154, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 113, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 3.0m, TopSpeedKmh = 305, FuelConsumptionCombined = 7.4m },
            ]);
            int f900r = GetOrCreateModel(bmwMotoId, "F 900 R", "bmw-f900r");
            AddEngines(GetOrCreateGeneration(f900r, "2020–", "bmw-f900r-2020", 2020, null), [
                new EngineVersion { EngineName = "895cc parallel-twin 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 895, FuelTypeId = ben,
                    TorqueNm = 92, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 3.8m, TopSpeedKmh = 215, FuelConsumptionCombined = 5.3m },
            ]);
        }

        // ── Suzuki Motorcycles ────────────────────────────────────────────────────
        {
            int suzMotoId = GetOrCreateBrand("Suzuki", "suzuki", "motocykle");
            int gsxr = GetOrCreateModel(suzMotoId, "GSX-R1000", "suzuki-gsx-r1000");
            AddEngines(GetOrCreateGeneration(gsxr, "L7 (2017–)", "suzuki-gsx-r1000-l7", 2017, null), [
                new EngineVersion { EngineName = "999cc inline-4 202 KM", PowerHP = 202, PowerKW = 149, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 117, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 3.0m, TopSpeedKmh = 299, FuelConsumptionCombined = 7.0m },
            ]);
            int vstrom = GetOrCreateModel(suzMotoId, "V-Strom 1050", "suzuki-vstrom1050");
            AddEngines(GetOrCreateGeneration(vstrom, "2020–", "suzuki-vstrom1050-2020", 2020, null), [
                new EngineVersion { EngineName = "1037cc V-Twin 107 KM", PowerHP = 107, PowerKW = 79, Displacement = 1037, FuelTypeId = ben,
                    TorqueNm = 100, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 2, Acceleration0100 = 4.0m, TopSpeedKmh = 220, FuelConsumptionCombined = 5.2m },
            ]);
        }

        // ── Abarth ────────────────────────────────────────────────────────────────
        {
            int abaId = GetOrCreateBrand("Abarth", "abarth", "osobowe");

            int ab500 = GetOrCreateModel(abaId, "500", "abarth-500");
            PrepareGenerations(ab500,
                ("I (2008–2015)", "abarth-500-i", 2008, 2015),
                ("II (2015–2016)", "abarth-500-ii", 2015, 2016));
            AddOrReplaceEngines(GetOrFixGeneration(ab500, "I (2008–2015)", "abarth-500-i", 2008, 2015), 130, [
                new EngineVersion { EngineName = "1.4 T-Jet 135 KM", PowerHP = 135, PowerKW = 99, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 180, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.9m, TopSpeedKmh = 205, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "1.4 T-Jet Esseesse 160 KM", PowerHP = 160, PowerKW = 118, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.4m, TopSpeedKmh = 213, FuelConsumptionCombined = 6.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(ab500, "II (2015–2016)", "abarth-500-ii", 2015, 2016), 130, [
                new EngineVersion { EngineName = "1.4 T-Jet 145 KM", PowerHP = 145, PowerKW = 107, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 206, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 213, FuelConsumptionCombined = 6.6m },
            ]);

            int ab500c = GetOrCreateModel(abaId, "500C", "abarth-500c");
            PrepareGenerations(ab500c,
                ("I (2010–2015)", "abarth-500c-i", 2010, 2015),
                ("II (2015–2016)", "abarth-500c-ii", 2015, 2016));
            AddOrReplaceEngines(GetOrFixGeneration(ab500c, "I (2010–2015)", "abarth-500c-i", 2010, 2015), 130, [
                new EngineVersion { EngineName = "1.4 T-Jet 135 KM", PowerHP = 135, PowerKW = 99, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 180, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.1m, TopSpeedKmh = 202, FuelConsumptionCombined = 6.6m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(ab500c, "II (2015–2016)", "abarth-500c-ii", 2015, 2016), 130, [
                new EngineVersion { EngineName = "1.4 T-Jet 145 KM", PowerHP = 145, PowerKW = 107, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 206, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.7m, TopSpeedKmh = 210, FuelConsumptionCombined = 6.7m },
            ]);

            int ab595 = GetOrCreateModel(abaId, "595", "abarth-595");
            PrepareGenerations(ab595,
                ("I (2016–2020)", "abarth-595-i", 2016, 2020),
                ("II (2020–2024)", "abarth-595-ii", 2020, 2024));
            AddOrReplaceEngines(GetOrFixGeneration(ab595, "I (2016–2020)", "abarth-595-i", 2016, 2020), 140, [
                new EngineVersion { EngineName = "1.4 T-Jet 145 KM", PowerHP = 145, PowerKW = 107, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 206, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 215, FuelConsumptionCombined = 6.9m },
                new EngineVersion { EngineName = "1.4 T-Jet Turismo 165 KM", PowerHP = 165, PowerKW = 121, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.0m, TopSpeedKmh = 218, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "1.4 T-Jet Competizione 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.7m, TopSpeedKmh = 225, FuelConsumptionCombined = 7.4m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(ab595, "II (2020–2024)", "abarth-595-ii", 2020, 2024), 140, [
                new EngineVersion { EngineName = "1.4 T-Jet 145 KM", PowerHP = 145, PowerKW = 107, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 206, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.4m, TopSpeedKmh = 216, FuelConsumptionCombined = 6.8m },
                new EngineVersion { EngineName = "1.4 T-Jet Turismo 165 KM", PowerHP = 165, PowerKW = 121, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.9m, TopSpeedKmh = 219, FuelConsumptionCombined = 6.9m },
                new EngineVersion { EngineName = "1.4 T-Jet Competizione 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.7m, TopSpeedKmh = 225, FuelConsumptionCombined = 7.3m },
            ]);

            int ab595c = GetOrCreateModel(abaId, "595C", "abarth-595c");
            AddOrReplaceEngines(GetOrFixGeneration(ab595c, "I (2016–2024)", "abarth-595c-i", 2016, 2024), 140, [
                new EngineVersion { EngineName = "1.4 T-Jet 145 KM", PowerHP = 145, PowerKW = 107, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 206, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.6m, TopSpeedKmh = 213, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "1.4 T-Jet Competizione 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.9m, TopSpeedKmh = 222, FuelConsumptionCombined = 7.5m },
            ]);

            int ab695 = GetOrCreateModel(abaId, "695", "abarth-695");
            PrepareGenerations(ab695,
                ("I (2018–2020)", "abarth-695-i", 2018, 2020),
                ("II (2020–2024)", "abarth-695-ii", 2020, 2024));
            AddOrReplaceEngines(GetOrFixGeneration(ab695, "I (2018–2020)", "abarth-695-i", 2018, 2020), 160, [
                new EngineVersion { EngineName = "1.4 T-Jet 70° Anniversario 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.7m, TopSpeedKmh = 225, FuelConsumptionCombined = 7.4m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(ab695, "II (2020–2024)", "abarth-695-ii", 2020, 2024), 160, [
                new EngineVersion { EngineName = "1.4 T-Jet Turismo 165 KM", PowerHP = 165, PowerKW = 121, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.9m, TopSpeedKmh = 219, FuelConsumptionCombined = 6.9m },
                new EngineVersion { EngineName = "1.4 T-Jet Esseesse 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.7m, TopSpeedKmh = 225, FuelConsumptionCombined = 7.3m },
            ]);

            int abGrandePunto = GetOrCreateModel(abaId, "Grande Punto", "abarth-grande-punto");
            AddOrReplaceEngines(GetOrFixGeneration(abGrandePunto, "I (2007–2010)", "abarth-grande-punto-i", 2007, 2010), 130, [
                new EngineVersion { EngineName = "1.4 T-Jet 155 KM", PowerHP = 155, PowerKW = 114, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 206, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.9m, TopSpeedKmh = 208, FuelConsumptionCombined = 7.2m },
                new EngineVersion { EngineName = "1.4 T-Jet SS 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 270, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 215, FuelConsumptionCombined = 7.6m },
            ]);

            int abPuntoEvo = GetOrCreateModel(abaId, "Punto Evo", "abarth-punto-evo");
            AddOrReplaceEngines(GetOrFixGeneration(abPuntoEvo, "I (2010–2012)", "abarth-punto-evo-i", 2010, 2012), 130, [
                new EngineVersion { EngineName = "1.4 T-Jet 165 KM", PowerHP = 165, PowerKW = 121, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 230, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.8m, TopSpeedKmh = 210, FuelConsumptionCombined = 7.1m },
            ]);

            int ab124Spider = GetOrCreateModel(abaId, "124 Spider", "abarth-124-spider");
            AddOrReplaceEngines(GetOrFixGeneration(ab124Spider, "I (2016–2019)", "abarth-124-spider-i", 2016, 2019), 150, [
                new EngineVersion { EngineName = "1.4 MultiAir Turbo 170 KM", PowerHP = 170, PowerKW = 125, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 6.8m, TopSpeedKmh = 232, FuelConsumptionCombined = 6.4m },
            ]);
        }

        // ── Dodge ─────────────────────────────────────────────────────────────────
        {
            int dodgeId = GetOrCreateBrand("Dodge", "dodge", "osobowe");
            int charger = GetOrCreateModel(dodgeId, "Charger", "dodge-charger");
            AddEngines(GetOrCreateGeneration(charger, "LX (2011–)", "dodge-charger-lx", 2011, null), [
                new EngineVersion { EngineName = "3.6 V6 Pentastar 300 KM", PowerHP = 300, PowerKW = 220, Displacement = 3604, FuelTypeId = ben,
                    TorqueNm = 353, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 6.6m, TopSpeedKmh = 210, FuelConsumptionCombined = 11.2m },
                new EngineVersion { EngineName = "5.7 V8 Hemi R/T 370 KM", PowerHP = 370, PowerKW = 272, Displacement = 5654, FuelTypeId = ben,
                    TorqueNm = 536, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 5.3m, TopSpeedKmh = 250, FuelConsumptionCombined = 13.8m },
                new EngineVersion { EngineName = "6.4 V8 SRT 485 KM", PowerHP = 485, PowerKW = 357, Displacement = 6417, FuelTypeId = ben,
                    TorqueNm = 644, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 4.6m, TopSpeedKmh = 275, FuelConsumptionCombined = 16.0m },
                new EngineVersion { EngineName = "6.2 V8 Hellcat 717 KM", PowerHP = 717, PowerKW = 527, Displacement = 6166, FuelTypeId = ben,
                    TorqueNm = 881, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.7m, TopSpeedKmh = 313, FuelConsumptionCombined = 18.8m },
            ]);
            int challenger = GetOrCreateModel(dodgeId, "Challenger", "dodge-challenger");
            AddEngines(GetOrCreateGeneration(challenger, "III (2008–)", "dodge-challenger-iii", 2008, null), [
                new EngineVersion { EngineName = "3.6 V6 305 KM", PowerHP = 305, PowerKW = 224, Displacement = 3604, FuelTypeId = ben,
                    TorqueNm = 363, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 7.0m, TopSpeedKmh = 203, FuelConsumptionCombined = 11.7m },
                new EngineVersion { EngineName = "5.7 V8 Hemi R/T 375 KM", PowerHP = 375, PowerKW = 276, Displacement = 5654, FuelTypeId = ben,
                    TorqueNm = 536, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 5.2m, TopSpeedKmh = 250, FuelConsumptionCombined = 13.9m },
                new EngineVersion { EngineName = "6.4 V8 SRT 392 KM 485 KM", PowerHP = 485, PowerKW = 357, Displacement = 6417, FuelTypeId = ben,
                    TorqueNm = 644, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 4.5m, TopSpeedKmh = 270, FuelConsumptionCombined = 15.9m },
                new EngineVersion { EngineName = "6.2 V8 Hellcat 717 KM", PowerHP = 717, PowerKW = 527, Displacement = 6166, FuelTypeId = ben,
                    TorqueNm = 881, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.6m, TopSpeedKmh = 328, FuelConsumptionCombined = 18.0m },
            ]);
            int durango = GetOrCreateModel(dodgeId, "Durango", "dodge-durango");
            AddEngines(GetOrCreateGeneration(durango, "III (2011–)", "dodge-durango-iii", 2011, null), [
                new EngineVersion { EngineName = "3.6 V6 Pentastar 293 KM", PowerHP = 293, PowerKW = 215, Displacement = 3604, FuelTypeId = ben,
                    TorqueNm = 353, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 7.5m, TopSpeedKmh = 200, FuelConsumptionCombined = 12.6m },
                new EngineVersion { EngineName = "5.7 V8 Hemi 360 KM", PowerHP = 360, PowerKW = 265, Displacement = 5654, FuelTypeId = ben,
                    TorqueNm = 530, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 6.5m, TopSpeedKmh = 210, FuelConsumptionCombined = 14.5m },
                new EngineVersion { EngineName = "6.4 V8 SRT 475 KM", PowerHP = 475, PowerKW = 349, Displacement = 6417, FuelTypeId = ben,
                    TorqueNm = 637, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.4m, TopSpeedKmh = 265, FuelConsumptionCombined = 17.6m },
            ]);

            int avenger = GetOrCreateModel(dodgeId, "Avenger", "dodge-avenger");
            AddOrReplaceEngines(GetOrFixGeneration(avenger, "I (2007–2014)", "dodge-avenger-i", 2007, 2014), 120, [
                new EngineVersion { EngineName = "2.0 156 KM", PowerHP = 156, PowerKW = 115, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 190, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 195, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "2.4 170 KM", PowerHP = 170, PowerKW = 125, Displacement = 2360, FuelTypeId = ben,
                    TorqueNm = 220, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.5m, TopSpeedKmh = 200, FuelConsumptionCombined = 9.0m },
                new EngineVersion { EngineName = "2.7 V6 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 2736, FuelTypeId = ben,
                    TorqueNm = 254, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 8.7m, TopSpeedKmh = 208, FuelConsumptionCombined = 10.2m },
                new EngineVersion { EngineName = "2.0 CRD 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.7m, TopSpeedKmh = 195, FuelConsumptionCombined = 6.0m },
            ]);

            int caliber = GetOrCreateModel(dodgeId, "Caliber", "dodge-caliber");
            AddOrReplaceEngines(GetOrFixGeneration(caliber, "I (2006–2012)", "dodge-caliber-i", 2006, 2012), 120, [
                new EngineVersion { EngineName = "1.8 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1798, FuelTypeId = ben,
                    TorqueNm = 172, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.9m, TopSpeedKmh = 190, FuelConsumptionCombined = 7.8m },
                new EngineVersion { EngineName = "2.0 156 KM", PowerHP = 156, PowerKW = 115, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 190, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 198, FuelConsumptionCombined = 8.3m },
                new EngineVersion { EngineName = "SRT4 2.4 Turbo 285 KM", PowerHP = 285, PowerKW = 210, Displacement = 2429, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.3m, TopSpeedKmh = 240, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "2.0 CRD 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 310, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 190, FuelConsumptionCombined = 6.0m },
            ]);

            int dakota = GetOrCreateModel(dodgeId, "Dakota", "dodge-dakota");
            PrepareGenerations(dakota,
                ("I (1987–1996)", "dodge-dakota-i", 1987, 1996),
                ("II (1997–2004)", "dodge-dakota-ii", 1997, 2004),
                ("III (2005–2011)", "dodge-dakota-iii", 2005, 2011));
            AddOrReplaceEngines(GetOrFixGeneration(dakota, "I (1987–1996)", "dodge-dakota-i", 1987, 1996), 100, [
                new EngineVersion { EngineName = "3.9 V6 125 KM", PowerHP = 125, PowerKW = 92, Displacement = 3908, FuelTypeId = ben,
                    TorqueNm = 271, EuroNorm = "Euro 1", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 11.5m, TopSpeedKmh = 160, FuelConsumptionCombined = 14.0m },
                new EngineVersion { EngineName = "5.2 V8 175 KM", PowerHP = 175, PowerKW = 129, Displacement = 5211, FuelTypeId = ben,
                    TorqueNm = 366, EuroNorm = "Euro 1", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 9.5m, TopSpeedKmh = 170, FuelConsumptionCombined = 16.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(dakota, "II (1997–2004)", "dodge-dakota-ii", 1997, 2004), 130, [
                new EngineVersion { EngineName = "3.9 V6 175 KM", PowerHP = 175, PowerKW = 129, Displacement = 3908, FuelTypeId = ben,
                    TorqueNm = 305, EuroNorm = "Euro 3", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 10.0m, TopSpeedKmh = 175, FuelConsumptionCombined = 14.5m },
                new EngineVersion { EngineName = "4.7 V8 235 KM", PowerHP = 235, PowerKW = 173, Displacement = 4701, FuelTypeId = ben,
                    TorqueNm = 407, EuroNorm = "Euro 3", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 8.1m, TopSpeedKmh = 185, FuelConsumptionCombined = 16.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(dakota, "III (2005–2011)", "dodge-dakota-iii", 2005, 2011), 150, [
                new EngineVersion { EngineName = "3.7 V6 210 KM", PowerHP = 210, PowerKW = 154, Displacement = 3701, FuelTypeId = ben,
                    TorqueNm = 319, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 9.6m, TopSpeedKmh = 180, FuelConsumptionCombined = 13.8m },
                new EngineVersion { EngineName = "4.7 V8 230 KM", PowerHP = 230, PowerKW = 169, Displacement = 4701, FuelTypeId = ben,
                    TorqueNm = 407, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 8.5m, TopSpeedKmh = 190, FuelConsumptionCombined = 15.8m },
            ]);

            int journey = GetOrCreateModel(dodgeId, "Journey", "dodge-journey");
            AddOrReplaceEngines(GetOrFixGeneration(journey, "I (2008–2020)", "dodge-journey-i", 2008, 2020), 130, [
                new EngineVersion { EngineName = "2.4 170 KM", PowerHP = 170, PowerKW = 125, Displacement = 2360, FuelTypeId = ben,
                    TorqueNm = 225, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.7m, TopSpeedKmh = 185, FuelConsumptionCombined = 9.0m },
                new EngineVersion { EngineName = "3.6 V6 283 KM", PowerHP = 283, PowerKW = 208, Displacement = 3604, FuelTypeId = ben,
                    TorqueNm = 348, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 8.2m, TopSpeedKmh = 200, FuelConsumptionCombined = 11.8m },
                new EngineVersion { EngineName = "2.0 CRD 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 180, FuelConsumptionCombined = 6.3m },
                new EngineVersion { EngineName = "2.0 CRD 170 KM", PowerHP = 170, PowerKW = 125, Displacement = 1956, FuelTypeId = die,
                    TorqueNm = 350, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 195, FuelConsumptionCombined = 6.6m },
            ]);

            int neon = GetOrCreateModel(dodgeId, "Neon", "dodge-neon");
            PrepareGenerations(neon,
                ("I (1994–1999)", "dodge-neon-i", 1994, 1999),
                ("II (1999–2005)", "dodge-neon-ii", 1999, 2005));
            AddOrReplaceEngines(GetOrFixGeneration(neon, "I (1994–1999)", "dodge-neon-i", 1994, 1999), 100, [
                new EngineVersion { EngineName = "2.0 132 KM", PowerHP = 132, PowerKW = 97, Displacement = 1996, FuelTypeId = ben,
                    TorqueNm = 176, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 195, FuelConsumptionCombined = 8.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(neon, "II (1999–2005)", "dodge-neon-ii", 1999, 2005), 100, [
                new EngineVersion { EngineName = "2.0 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1996, FuelTypeId = ben,
                    TorqueNm = 180, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.5m, TopSpeedKmh = 198, FuelConsumptionCombined = 8.3m },
                new EngineVersion { EngineName = "SRT-4 2.4 Turbo 230 KM", PowerHP = 230, PowerKW = 169, Displacement = 2429, FuelTypeId = ben,
                    TorqueNm = 353, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.0m, TopSpeedKmh = 240, FuelConsumptionCombined = 10.0m },
            ]);

            int nitro = GetOrCreateModel(dodgeId, "Nitro", "dodge-nitro");
            AddOrReplaceEngines(GetOrFixGeneration(nitro, "I (2006–2011)", "dodge-nitro-i", 2006, 2011), 130, [
                new EngineVersion { EngineName = "2.8 CRD 177 KM", PowerHP = 177, PowerKW = 130, Displacement = 2777, FuelTypeId = die,
                    TorqueNm = 360, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 10.8m, TopSpeedKmh = 175, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "3.7 V6 210 KM", PowerHP = 210, PowerKW = 154, Displacement = 3701, FuelTypeId = ben,
                    TorqueNm = 319, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 9.5m, TopSpeedKmh = 180, FuelConsumptionCombined = 13.5m },
                new EngineVersion { EngineName = "4.0 V6 235 KM", PowerHP = 235, PowerKW = 173, Displacement = 3954, FuelTypeId = ben,
                    TorqueNm = 342, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 8.6m, TopSpeedKmh = 190, FuelConsumptionCombined = 14.5m },
            ]);

            int ram1500 = GetOrCreateModel(dodgeId, "Ram 1500", "dodge-ram-1500");
            PrepareGenerations(ram1500,
                ("III (2001–2008)", "dodge-ram-1500-iii", 2001, 2008),
                ("V (2019–)", "dodge-ram-1500-v", 2019, null));
            AddOrReplaceEngines(GetOrFixGeneration(ram1500, "III (2001–2008)", "dodge-ram-1500-iii", 2001, 2008), 200, [
                new EngineVersion { EngineName = "3.7 V6 215 KM", PowerHP = 215, PowerKW = 158, Displacement = 3701, FuelTypeId = ben,
                    TorqueNm = 319, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 9.6m, TopSpeedKmh = 175, FuelConsumptionCombined = 15.5m },
                new EngineVersion { EngineName = "4.7 V8 235 KM", PowerHP = 235, PowerKW = 173, Displacement = 4701, FuelTypeId = ben,
                    TorqueNm = 407, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 8.5m, TopSpeedKmh = 185, FuelConsumptionCombined = 17.0m },
                new EngineVersion { EngineName = "5.7 V8 Hemi 345 KM", PowerHP = 345, PowerKW = 254, Displacement = 5654, FuelTypeId = ben,
                    TorqueNm = 529, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 7.2m, TopSpeedKmh = 195, FuelConsumptionCombined = 18.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(ram1500, "V (2019–)", "dodge-ram-1500-v", 2019, null), 250, [
                new EngineVersion { EngineName = "3.6 V6 305 KM", PowerHP = 305, PowerKW = 224, Displacement = 3604, FuelTypeId = ben,
                    TorqueNm = 361, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 7.9m, TopSpeedKmh = 180, FuelConsumptionCombined = 13.5m },
                new EngineVersion { EngineName = "5.7 V8 Hemi 395 KM", PowerHP = 395, PowerKW = 290, Displacement = 5654, FuelTypeId = ben,
                    TorqueNm = 556, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 6.3m, TopSpeedKmh = 200, FuelConsumptionCombined = 15.8m },
                new EngineVersion { EngineName = "3.0 EcoDiesel 260 KM", PowerHP = 260, PowerKW = 191, Displacement = 2987, FuelTypeId = die,
                    TorqueNm = 570, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 7.8m, TopSpeedKmh = 185, FuelConsumptionCombined = 9.5m },
            ]);

            int viper = GetOrCreateModel(dodgeId, "Viper", "dodge-viper");
            PrepareGenerations(viper,
                ("I (1992–2002)", "dodge-viper-i", 1992, 2002),
                ("II (2003–2010)", "dodge-viper-ii", 2003, 2010),
                ("III (2013–2017)", "dodge-viper-iii", 2013, 2017));
            AddOrReplaceEngines(GetOrFixGeneration(viper, "I (1992–2002)", "dodge-viper-i", 1992, 2002), 350, [
                new EngineVersion { EngineName = "8.0 V10 400 KM", PowerHP = 400, PowerKW = 294, Displacement = 7990, FuelTypeId = ben,
                    TorqueNm = 630, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 10, Acceleration0100 = 4.6m, TopSpeedKmh = 266, FuelConsumptionCombined = 19.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(viper, "II (2003–2010)", "dodge-viper-ii", 2003, 2010), 450, [
                new EngineVersion { EngineName = "8.3 V10 SRT-10 510 KM", PowerHP = 510, PowerKW = 375, Displacement = 8285, FuelTypeId = ben,
                    TorqueNm = 712, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 10, Acceleration0100 = 3.9m, TopSpeedKmh = 314, FuelConsumptionCombined = 20.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(viper, "III (2013–2017)", "dodge-viper-iii", 2013, 2017), 550, [
                new EngineVersion { EngineName = "8.4 V10 GTS 649 KM", PowerHP = 649, PowerKW = 477, Displacement = 8382, FuelTypeId = ben,
                    TorqueNm = 813, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 10, Acceleration0100 = 3.4m, TopSpeedKmh = 330, FuelConsumptionCombined = 21.5m },
            ]);
        }

        // ── Chrysler ──────────────────────────────────────────────────────────────
        {
            int chrId = GetOrCreateBrand("Chrysler", "chrysler", "osobowe");
            int c300 = GetOrCreateModel(chrId, "300C", "chrysler-300c");
            AddEngines(GetOrCreateGeneration(c300, "II (2011–)", "chrysler-300c-ii", 2011, null), [
                new EngineVersion { EngineName = "3.6 V6 Pentastar 292 KM", PowerHP = 292, PowerKW = 215, Displacement = 3604, FuelTypeId = ben,
                    TorqueNm = 353, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 7.3m, TopSpeedKmh = 210, FuelConsumptionCombined = 11.5m },
                new EngineVersion { EngineName = "5.7 V8 Hemi 363 KM", PowerHP = 363, PowerKW = 267, Displacement = 5654, FuelTypeId = ben,
                    TorqueNm = 530, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 6.1m, TopSpeedKmh = 225, FuelConsumptionCombined = 13.9m },
                new EngineVersion { EngineName = "3.0 CRD V6 239 KM", PowerHP = 239, PowerKW = 176, Displacement = 2987, FuelTypeId = die,
                    TorqueNm = 550, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 7.6m, TopSpeedKmh = 215, FuelConsumptionCombined = 7.6m },
            ]);
            int pacifica = GetOrCreateModel(chrId, "Pacifica", "chrysler-pacifica");
            PrepareGenerations(pacifica,
                ("I (2003–2008)", "chrysler-pacifica-i", 2003, 2008),
                ("II (2016–)", "chrysler-pacifica-ii", 2016, null));
            AddOrReplaceEngines(GetOrFixGeneration(pacifica, "I (2003–2008)", "chrysler-pacifica-i", 2003, 2008), 200, [
                new EngineVersion { EngineName = "3.5 V6 250 KM", PowerHP = 250, PowerKW = 184, Displacement = 3518, FuelTypeId = ben,
                    TorqueNm = 322, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 8.5m, TopSpeedKmh = 190, FuelConsumptionCombined = 13.5m },
                new EngineVersion { EngineName = "4.0 V6 253 KM", PowerHP = 253, PowerKW = 186, Displacement = 3952, FuelTypeId = ben,
                    TorqueNm = 346, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 8.2m, TopSpeedKmh = 195, FuelConsumptionCombined = 13.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(pacifica, "II (2016–)", "chrysler-pacifica-ii", 2016, null), 200, [
                new EngineVersion { EngineName = "3.6 V6 Pentastar 287 KM", PowerHP = 287, PowerKW = 211, Displacement = 3604, FuelTypeId = ben,
                    TorqueNm = 353, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 7.8m, TopSpeedKmh = 195, FuelConsumptionCombined = 12.3m },
                new EngineVersion { EngineName = "3.6 V6 PHEV 260 KM", PowerHP = 260, PowerKW = 191, Displacement = 3604, FuelTypeId = phev,
                    TorqueNm = 353, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 8.5m, TopSpeedKmh = 185, FuelConsumptionCombined = 3.3m },
            ]);

            int c300plain = GetOrCreateModel(chrId, "300", "chrysler-300");
            PrepareGenerations(c300plain,
                ("I (2005–2010)", "chrysler-300-i", 2005, 2010),
                ("II (2011–2023)", "chrysler-300-ii", 2011, 2023));
            AddOrReplaceEngines(GetOrFixGeneration(c300plain, "I (2005–2010)", "chrysler-300-i", 2005, 2010), 150, [
                new EngineVersion { EngineName = "2.7 V6 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 2736, FuelTypeId = ben,
                    TorqueNm = 258, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 9.5m, TopSpeedKmh = 200, FuelConsumptionCombined = 12.0m },
                new EngineVersion { EngineName = "3.5 V6 250 KM", PowerHP = 250, PowerKW = 184, Displacement = 3518, FuelTypeId = ben,
                    TorqueNm = 340, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 7.7m, TopSpeedKmh = 220, FuelConsumptionCombined = 12.8m },
                new EngineVersion { EngineName = "300C 5.7 V8 Hemi 340 KM", PowerHP = 340, PowerKW = 250, Displacement = 5654, FuelTypeId = ben,
                    TorqueNm = 525, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 6.3m, TopSpeedKmh = 240, FuelConsumptionCombined = 15.5m },
                new EngineVersion { EngineName = "SRT8 6.1 V8 425 KM", PowerHP = 425, PowerKW = 313, Displacement = 6059, FuelTypeId = ben,
                    TorqueNm = 569, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 5.1m, TopSpeedKmh = 274, FuelConsumptionCombined = 17.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(c300plain, "II (2011–2023)", "chrysler-300-ii", 2011, 2023), 200, [
                new EngineVersion { EngineName = "3.6 V6 292 KM", PowerHP = 292, PowerKW = 215, Displacement = 3604, FuelTypeId = ben,
                    TorqueNm = 353, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 7.3m, TopSpeedKmh = 210, FuelConsumptionCombined = 11.5m },
                new EngineVersion { EngineName = "300C 5.7 V8 Hemi 363 KM", PowerHP = 363, PowerKW = 267, Displacement = 5654, FuelTypeId = ben,
                    TorqueNm = 530, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 6.1m, TopSpeedKmh = 225, FuelConsumptionCombined = 13.9m },
                new EngineVersion { EngineName = "SRT 6.4 V8 485 KM", PowerHP = 485, PowerKW = 357, Displacement = 6417, FuelTypeId = ben,
                    TorqueNm = 631, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 4.9m, TopSpeedKmh = 282, FuelConsumptionCombined = 16.0m },
            ]);

            int crossfire = GetOrCreateModel(chrId, "Crossfire", "chrysler-crossfire");
            AddOrReplaceEngines(GetOrFixGeneration(crossfire, "I (2003–2008)", "chrysler-crossfire-i", 2003, 2008), 150, [
                new EngineVersion { EngineName = "3.2 V6 215 KM", PowerHP = 215, PowerKW = 158, Displacement = 3199, FuelTypeId = ben,
                    TorqueNm = 310, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 6.9m, TopSpeedKmh = 240, FuelConsumptionCombined = 11.0m },
                new EngineVersion { EngineName = "SRT-6 3.2 Supercharged 335 KM", PowerHP = 335, PowerKW = 246, Displacement = 3199, FuelTypeId = ben,
                    TorqueNm = 420, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 5.2m, TopSpeedKmh = 258, FuelConsumptionCombined = 12.8m },
            ]);

            int ptCruiser = GetOrCreateModel(chrId, "PT Cruiser", "chrysler-pt-cruiser");
            AddOrReplaceEngines(GetOrFixGeneration(ptCruiser, "I (2000–2010)", "chrysler-pt-cruiser-i", 2000, 2010), 100, [
                new EngineVersion { EngineName = "2.0 141 KM", PowerHP = 141, PowerKW = 104, Displacement = 1996, FuelTypeId = ben,
                    TorqueNm = 180, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 178, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "2.4 Turbo GT 223 KM", PowerHP = 223, PowerKW = 164, Displacement = 2429, FuelTypeId = ben,
                    TorqueNm = 305, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.2m, TopSpeedKmh = 205, FuelConsumptionCombined = 9.8m },
                new EngineVersion { EngineName = "2.2 CRD 121 KM", PowerHP = 121, PowerKW = 89, Displacement = 2148, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.6m, TopSpeedKmh = 175, FuelConsumptionCombined = 6.2m },
            ]);

            int sebring = GetOrCreateModel(chrId, "Sebring", "chrysler-sebring");
            PrepareGenerations(sebring,
                ("I (1995–2000)", "chrysler-sebring-i", 1995, 2000),
                ("II (2001–2006)", "chrysler-sebring-ii", 2001, 2006),
                ("III (2007–2010)", "chrysler-sebring-iii", 2007, 2010));
            AddOrReplaceEngines(GetOrFixGeneration(sebring, "I (1995–2000)", "chrysler-sebring-i", 1995, 2000), 100, [
                new EngineVersion { EngineName = "2.0 137 KM", PowerHP = 137, PowerKW = 101, Displacement = 1996, FuelTypeId = ben,
                    TorqueNm = 176, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.8m, TopSpeedKmh = 195, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "2.5 V6 168 KM", PowerHP = 168, PowerKW = 124, Displacement = 2497, FuelTypeId = ben,
                    TorqueNm = 218, EuroNorm = "Euro 2", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 9.2m, TopSpeedKmh = 205, FuelConsumptionCombined = 9.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(sebring, "II (2001–2006)", "chrysler-sebring-ii", 2001, 2006), 130, [
                new EngineVersion { EngineName = "2.4 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 2429, FuelTypeId = ben,
                    TorqueNm = 216, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.0m, TopSpeedKmh = 195, FuelConsumptionCombined = 8.3m },
                new EngineVersion { EngineName = "2.7 V6 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 2736, FuelTypeId = ben,
                    TorqueNm = 254, EuroNorm = "Euro 3", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 8.5m, TopSpeedKmh = 210, FuelConsumptionCombined = 9.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(sebring, "III (2007–2010)", "chrysler-sebring-iii", 2007, 2010), 150, [
                new EngineVersion { EngineName = "2.4 173 KM", PowerHP = 173, PowerKW = 127, Displacement = 2360, FuelTypeId = ben,
                    TorqueNm = 220, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 195, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "2.7 V6 189 KM", PowerHP = 189, PowerKW = 139, Displacement = 2736, FuelTypeId = ben,
                    TorqueNm = 252, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 9.0m, TopSpeedKmh = 205, FuelConsumptionCombined = 10.2m },
                new EngineVersion { EngineName = "2.0 CRD 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 190, FuelConsumptionCombined = 6.2m },
            ]);

            int townCountry = GetOrCreateModel(chrId, "Town & Country", "chrysler-town-country");
            PrepareGenerations(townCountry,
                ("IV (2001–2007)", "chrysler-town-country-iv", 2001, 2007),
                ("V (2008–2016)", "chrysler-town-country-v", 2008, 2016));
            AddOrReplaceEngines(GetOrFixGeneration(townCountry, "IV (2001–2007)", "chrysler-town-country-iv", 2001, 2007), 150, [
                new EngineVersion { EngineName = "3.3 V6 182 KM", PowerHP = 182, PowerKW = 134, Displacement = 3301, FuelTypeId = ben,
                    TorqueNm = 265, EuroNorm = "Euro 3", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 11.0m, TopSpeedKmh = 175, FuelConsumptionCombined = 12.5m },
                new EngineVersion { EngineName = "3.8 V6 215 KM", PowerHP = 215, PowerKW = 158, Displacement = 3778, FuelTypeId = ben,
                    TorqueNm = 322, EuroNorm = "Euro 3", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 9.6m, TopSpeedKmh = 185, FuelConsumptionCombined = 13.2m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(townCountry, "V (2008–2016)", "chrysler-town-country-v", 2008, 2016), 200, [
                new EngineVersion { EngineName = "3.6 V6 Pentastar 283 KM", PowerHP = 283, PowerKW = 208, Displacement = 3604, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 8.4m, TopSpeedKmh = 195, FuelConsumptionCombined = 12.8m },
                new EngineVersion { EngineName = "4.0 V6 251 KM", PowerHP = 251, PowerKW = 185, Displacement = 3952, FuelTypeId = ben,
                    TorqueNm = 346, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 8.9m, TopSpeedKmh = 190, FuelConsumptionCombined = 13.5m },
            ]);

            int voyager = GetOrCreateModel(chrId, "Voyager", "chrysler-voyager");
            PrepareGenerations(voyager,
                ("I (1988–1995)", "chrysler-voyager-i", 1988, 1995),
                ("IV (2001–2007)", "chrysler-voyager-iv", 2001, 2007));
            AddOrReplaceEngines(GetOrFixGeneration(voyager, "I (1988–1995)", "chrysler-voyager-i", 1988, 1995), 80, [
                new EngineVersion { EngineName = "2.5 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 2500, FuelTypeId = ben,
                    TorqueNm = 183, EuroNorm = "Euro 1", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.0m, TopSpeedKmh = 160, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "3.0 V6 142 KM", PowerHP = 142, PowerKW = 104, Displacement = 2972, FuelTypeId = ben,
                    TorqueNm = 224, EuroNorm = "Euro 1", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 11.2m, TopSpeedKmh = 175, FuelConsumptionCombined = 11.8m },
                new EngineVersion { EngineName = "2.5 TD 116 KM", PowerHP = 116, PowerKW = 85, Displacement = 2500, FuelTypeId = die,
                    TorqueNm = 260, EuroNorm = "Euro 1", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.8m, TopSpeedKmh = 158, FuelConsumptionCombined = 8.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(voyager, "IV (2001–2007)", "chrysler-voyager-iv", 2001, 2007), 130, [
                new EngineVersion { EngineName = "2.4 147 KM", PowerHP = 147, PowerKW = 108, Displacement = 2429, FuelTypeId = ben,
                    TorqueNm = 217, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.0m, TopSpeedKmh = 175, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "3.3 V6 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 3301, FuelTypeId = ben,
                    TorqueNm = 265, EuroNorm = "Euro 3", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 10.8m, TopSpeedKmh = 180, FuelConsumptionCombined = 12.5m },
                new EngineVersion { EngineName = "2.5 CRD 141 KM", PowerHP = 141, PowerKW = 104, Displacement = 2477, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 5, Acceleration0100 = 12.5m, TopSpeedKmh = 170, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "2.8 CRD 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 2776, FuelTypeId = die,
                    TorqueNm = 315, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.0m, TopSpeedKmh = 172, FuelConsumptionCombined = 8.2m },
            ]);
        }

        // ── Chevrolet ─────────────────────────────────────────────────────────────
        {
            int chevId = GetOrCreateBrand("Chevrolet", "chevrolet", "osobowe");
            int camaro = GetOrCreateModel(chevId, "Camaro", "chevrolet-camaro");
            PrepareGenerations(camaro,
                ("IV (1993–2002)", "chevrolet-camaro-iv", 1993, 2002),
                ("V (2010–2015)", "chevrolet-camaro-v", 2010, 2015));
            AddOrReplaceEngines(GetOrFixGeneration(camaro, "IV (1993–2002)", "chevrolet-camaro-iv", 1993, 2002), 150, [
                new EngineVersion { EngineName = "3.4 V6 160 KM", PowerHP = 160, PowerKW = 118, Displacement = 3350, FuelTypeId = ben,
                    TorqueNm = 285, EuroNorm = "Euro 2", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 9.0m, TopSpeedKmh = 190, FuelConsumptionCombined = 12.5m },
                new EngineVersion { EngineName = "3.8 V6 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 3791, FuelTypeId = ben,
                    TorqueNm = 305, EuroNorm = "Euro 2", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 7.6m, TopSpeedKmh = 205, FuelConsumptionCombined = 13.2m },
                new EngineVersion { EngineName = "5.7 V8 LS1 305 KM", PowerHP = 305, PowerKW = 224, Displacement = 5665, FuelTypeId = ben,
                    TorqueNm = 461, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 5.5m, TopSpeedKmh = 250, FuelConsumptionCombined = 15.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(camaro, "V (2010–2015)", "chevrolet-camaro-v", 2010, 2015), 250, [
                new EngineVersion { EngineName = "3.6 V6 312 KM", PowerHP = 312, PowerKW = 229, Displacement = 3564, FuelTypeId = ben,
                    TorqueNm = 373, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 5.9m, TopSpeedKmh = 250, FuelConsumptionCombined = 11.5m },
                new EngineVersion { EngineName = "6.2 V8 SS 432 KM", PowerHP = 432, PowerKW = 318, Displacement = 6162, FuelTypeId = ben,
                    TorqueNm = 557, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 4.6m, TopSpeedKmh = 250, FuelConsumptionCombined = 15.6m },
                new EngineVersion { EngineName = "6.2 V8 ZL1 580 KM", PowerHP = 580, PowerKW = 427, Displacement = 6162, FuelTypeId = ben,
                    TorqueNm = 754, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.9m, TopSpeedKmh = 300, FuelConsumptionCombined = 16.2m },
            ]);
            AddEngines(GetOrCreateGeneration(camaro, "VI (2015–)", "chevrolet-camaro-vi", 2015, null), [
                new EngineVersion { EngineName = "2.0 Turbo 275 KM", PowerHP = 275, PowerKW = 202, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 5.4m, TopSpeedKmh = 244, FuelConsumptionCombined = 9.2m },
                new EngineVersion { EngineName = "3.6 V6 340 KM", PowerHP = 340, PowerKW = 250, Displacement = 3649, FuelTypeId = ben,
                    TorqueNm = 386, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 5.4m, TopSpeedKmh = 250, FuelConsumptionCombined = 11.3m },
                new EngineVersion { EngineName = "6.2 V8 SS 453 KM", PowerHP = 453, PowerKW = 333, Displacement = 6162, FuelTypeId = ben,
                    TorqueNm = 617, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 4.0m, TopSpeedKmh = 280, FuelConsumptionCombined = 15.4m },
                new EngineVersion { EngineName = "6.2 V8 ZL1 650 KM", PowerHP = 650, PowerKW = 478, Displacement = 6162, FuelTypeId = ben,
                    TorqueNm = 868, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.5m, TopSpeedKmh = 320, FuelConsumptionCombined = 17.0m },
            ]);
            int equinox = GetOrCreateModel(chevId, "Equinox", "chevrolet-equinox");
            AddEngines(GetOrCreateGeneration(equinox, "III (2017–)", "chevrolet-equinox-iii", 2017, null), [
                new EngineVersion { EngineName = "1.5 Turbo 170 KM", PowerHP = 170, PowerKW = 125, Displacement = 1490, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 190, FuelConsumptionCombined = 8.2m },
                new EngineVersion { EngineName = "2.0 Turbo 252 KM", PowerHP = 252, PowerKW = 185, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 353, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.1m, TopSpeedKmh = 210, FuelConsumptionCombined = 9.0m },
                new EngineVersion { EngineName = "1.6 CDTI 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.7m, TopSpeedKmh = 190, FuelConsumptionCombined = 5.5m },
            ]);
            int corvette = GetOrCreateModel(chevId, "Corvette", "chevrolet-corvette");
            AddEngines(GetOrCreateGeneration(corvette, "C7 (2013–2019)", "chevrolet-corvette-c7", 2013, 2019), [
                new EngineVersion { EngineName = "6.2 V8 LT1 466 KM", PowerHP = 466, PowerKW = 343, Displacement = 6162, FuelTypeId = ben,
                    TorqueNm = 630, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.8m, TopSpeedKmh = 290, FuelConsumptionCombined = 14.9m },
                new EngineVersion { EngineName = "6.2 V8 Z06 659 KM", PowerHP = 659, PowerKW = 485, Displacement = 6162, FuelTypeId = ben,
                    TorqueNm = 880, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.2m, TopSpeedKmh = 305, FuelConsumptionCombined = 16.1m },
            ]);
            AddEngines(GetOrCreateGeneration(corvette, "C8 (2020–)", "chevrolet-corvette-c8", 2020, null), [
                new EngineVersion { EngineName = "6.2 V8 LT2 502 KM", PowerHP = 502, PowerKW = 369, Displacement = 6162, FuelTypeId = ben,
                    TorqueNm = 637, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 2.9m, TopSpeedKmh = 312, FuelConsumptionCombined = 15.5m },
                new EngineVersion { EngineName = "5.5 V8 Z06 680 KM", PowerHP = 680, PowerKW = 500, Displacement = 5532, FuelTypeId = ben,
                    TorqueNm = 637, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 2.6m, TopSpeedKmh = 335, FuelConsumptionCombined = 16.5m },
            ]);

            int blazer = GetOrCreateModel(chevId, "Blazer", "chevrolet-blazer");
            PrepareGenerations(blazer,
                ("I (2019–2023)", "chevrolet-blazer-i", 2019, 2023),
                ("II (2024–)", "chevrolet-blazer-ii", 2024, null));
            AddOrReplaceEngines(GetOrFixGeneration(blazer, "I (2019–2023)", "chevrolet-blazer-i", 2019, 2023), 150, [
                new EngineVersion { EngineName = "2.5 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 2496, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.0m, TopSpeedKmh = 190, FuelConsumptionCombined = 9.5m },
                new EngineVersion { EngineName = "RS 2.0T 233 KM", PowerHP = 233, PowerKW = 171, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 353, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.2m, TopSpeedKmh = 200, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "3.6 V6 308 KM", PowerHP = 308, PowerKW = 226, Displacement = 3649, FuelTypeId = ben,
                    TorqueNm = 362, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.6m, TopSpeedKmh = 210, FuelConsumptionCombined = 11.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(blazer, "II (2024–)", "chevrolet-blazer-ii", 2024, null), 150, [
                new EngineVersion { EngineName = "2.0T 233 KM", PowerHP = 233, PowerKW = 171, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 353, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.0m, TopSpeedKmh = 200, FuelConsumptionCombined = 10.2m },
                new EngineVersion { EngineName = "3.6 V6 308 KM", PowerHP = 308, PowerKW = 226, Displacement = 3649, FuelTypeId = ben,
                    TorqueNm = 362, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.5m, TopSpeedKmh = 210, FuelConsumptionCombined = 11.6m },
            ]);

            int colorado = GetOrCreateModel(chevId, "Colorado", "chevrolet-colorado");
            PrepareGenerations(colorado,
                ("I (2004–2012)", "chevrolet-colorado-i", 2004, 2012),
                ("II (2012–)", "chevrolet-colorado-ii", 2012, null));
            AddOrReplaceEngines(GetOrFixGeneration(colorado, "I (2004–2012)", "chevrolet-colorado-i", 2004, 2012), 130, [
                new EngineVersion { EngineName = "2.8 I4 175 KM", PowerHP = 175, PowerKW = 129, Displacement = 2836, FuelTypeId = ben,
                    TorqueNm = 244, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 170, FuelConsumptionCombined = 11.5m },
                new EngineVersion { EngineName = "3.5 I5 220 KM", PowerHP = 220, PowerKW = 162, Displacement = 3456, FuelTypeId = ben,
                    TorqueNm = 305, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 5, Acceleration0100 = 9.0m, TopSpeedKmh = 175, FuelConsumptionCombined = 13.0m },
                new EngineVersion { EngineName = "5.3 V8 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 5328, FuelTypeId = ben,
                    TorqueNm = 460, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 7.8m, TopSpeedKmh = 180, FuelConsumptionCombined = 15.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(colorado, "II (2012–)", "chevrolet-colorado-ii", 2012, null), 150, [
                new EngineVersion { EngineName = "2.5 I4 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 2457, FuelTypeId = ben,
                    TorqueNm = 258, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 175, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "3.6 V6 305 KM", PowerHP = 305, PowerKW = 224, Displacement = 3564, FuelTypeId = ben,
                    TorqueNm = 366, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 7.5m, TopSpeedKmh = 185, FuelConsumptionCombined = 12.8m },
                new EngineVersion { EngineName = "2.8 Duramax Diesel 181 KM", PowerHP = 181, PowerKW = 133, Displacement = 2776, FuelTypeId = die,
                    TorqueNm = 500, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 170, FuelConsumptionCombined = 8.5m },
            ]);

            int cruze = GetOrCreateModel(chevId, "Cruze", "chevrolet-cruze");
            PrepareGenerations(cruze,
                ("I (2009–2015)", "chevrolet-cruze-i", 2009, 2015),
                ("II (2015–2019)", "chevrolet-cruze-ii", 2015, 2019));
            AddOrReplaceEngines(GetOrFixGeneration(cruze, "I (2009–2015)", "chevrolet-cruze-i", 2009, 2015), 100, [
                new EngineVersion { EngineName = "1.6 109 KM", PowerHP = 109, PowerKW = 80, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 150, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.1m, TopSpeedKmh = 185, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "1.8 141 KM", PowerHP = 141, PowerKW = 104, Displacement = 1796, FuelTypeId = ben,
                    TorqueNm = 176, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 200, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "1.4T 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1364, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.4m, TopSpeedKmh = 205, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "2.0D 163 KM", PowerHP = 163, PowerKW = 120, Displacement = 1956, FuelTypeId = die,
                    TorqueNm = 360, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.7m, TopSpeedKmh = 210, FuelConsumptionCombined = 4.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(cruze, "II (2015–2019)", "chevrolet-cruze-ii", 2015, 2019), 100, [
                new EngineVersion { EngineName = "1.4T 153 KM", PowerHP = 153, PowerKW = 113, Displacement = 1364, FuelTypeId = ben,
                    TorqueNm = 240, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.0m, TopSpeedKmh = 210, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "1.6D 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 205, FuelConsumptionCombined = 4.3m },
            ]);

            int silverado = GetOrCreateModel(chevId, "Silverado", "chevrolet-silverado");
            PrepareGenerations(silverado,
                ("I (1999–2006)", "chevrolet-silverado-i", 1999, 2006),
                ("II (2007–2013)", "chevrolet-silverado-ii", 2007, 2013),
                ("III (2014–2018)", "chevrolet-silverado-iii", 2014, 2018),
                ("IV (2019–)", "chevrolet-silverado-iv", 2019, null));
            AddOrReplaceEngines(GetOrFixGeneration(silverado, "I (1999–2006)", "chevrolet-silverado-i", 1999, 2006), 200, [
                new EngineVersion { EngineName = "4.8 V8 285 KM", PowerHP = 285, PowerKW = 210, Displacement = 4826, FuelTypeId = ben,
                    TorqueNm = 407, EuroNorm = "Euro 3", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 8.5m, TopSpeedKmh = 170, FuelConsumptionCombined = 16.0m },
                new EngineVersion { EngineName = "5.3 V8 295 KM", PowerHP = 295, PowerKW = 217, Displacement = 5327, FuelTypeId = ben,
                    TorqueNm = 447, EuroNorm = "Euro 3", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 8.0m, TopSpeedKmh = 175, FuelConsumptionCombined = 16.5m },
                new EngineVersion { EngineName = "6.0 V8 325 KM", PowerHP = 325, PowerKW = 239, Displacement = 5967, FuelTypeId = ben,
                    TorqueNm = 542, EuroNorm = "Euro 3", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 7.5m, TopSpeedKmh = 180, FuelConsumptionCombined = 17.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(silverado, "II (2007–2013)", "chevrolet-silverado-ii", 2007, 2013), 250, [
                new EngineVersion { EngineName = "4.8 V8 302 KM", PowerHP = 302, PowerKW = 222, Displacement = 4826, FuelTypeId = ben,
                    TorqueNm = 434, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 8.2m, TopSpeedKmh = 175, FuelConsumptionCombined = 15.5m },
                new EngineVersion { EngineName = "5.3 V8 315 KM", PowerHP = 315, PowerKW = 232, Displacement = 5327, FuelTypeId = ben,
                    TorqueNm = 474, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 7.8m, TopSpeedKmh = 180, FuelConsumptionCombined = 16.0m },
                new EngineVersion { EngineName = "6.2 V8 403 KM", PowerHP = 403, PowerKW = 296, Displacement = 6162, FuelTypeId = ben,
                    TorqueNm = 583, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 6.8m, TopSpeedKmh = 190, FuelConsumptionCombined = 17.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(silverado, "III (2014–2018)", "chevrolet-silverado-iii", 2014, 2018), 250, [
                new EngineVersion { EngineName = "4.3 V6 285 KM", PowerHP = 285, PowerKW = 210, Displacement = 4300, FuelTypeId = ben,
                    TorqueNm = 373, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 8.6m, TopSpeedKmh = 175, FuelConsumptionCombined = 13.5m },
                new EngineVersion { EngineName = "5.3 V8 355 KM", PowerHP = 355, PowerKW = 261, Displacement = 5328, FuelTypeId = ben,
                    TorqueNm = 519, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 6.9m, TopSpeedKmh = 185, FuelConsumptionCombined = 14.8m },
                new EngineVersion { EngineName = "6.2 V8 420 KM", PowerHP = 420, PowerKW = 309, Displacement = 6162, FuelTypeId = ben,
                    TorqueNm = 624, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 6.2m, TopSpeedKmh = 195, FuelConsumptionCombined = 16.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(silverado, "IV (2019–)", "chevrolet-silverado-iv", 2019, null), 250, [
                new EngineVersion { EngineName = "4.3 V6 285 KM", PowerHP = 285, PowerKW = 210, Displacement = 4300, FuelTypeId = ben,
                    TorqueNm = 373, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 8.5m, TopSpeedKmh = 175, FuelConsumptionCombined = 13.2m },
                new EngineVersion { EngineName = "5.3 V8 355 KM", PowerHP = 355, PowerKW = 261, Displacement = 5328, FuelTypeId = ben,
                    TorqueNm = 519, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 6.8m, TopSpeedKmh = 185, FuelConsumptionCombined = 14.5m },
                new EngineVersion { EngineName = "6.2 V8 420 KM", PowerHP = 420, PowerKW = 309, Displacement = 6162, FuelTypeId = ben,
                    TorqueNm = 624, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 6.0m, TopSpeedKmh = 195, FuelConsumptionCombined = 16.2m },
                new EngineVersion { EngineName = "3.0 Duramax Diesel 277 KM", PowerHP = 277, PowerKW = 204, Displacement = 2993, FuelTypeId = die,
                    TorqueNm = 623, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 7.8m, TopSpeedKmh = 180, FuelConsumptionCombined = 9.5m },
            ]);

            int spark = GetOrCreateModel(chevId, "Spark", "chevrolet-spark");
            PrepareGenerations(spark,
                ("I (2009–2015)", "chevrolet-spark-i", 2009, 2015),
                ("II (2015–2022)", "chevrolet-spark-ii", 2015, 2022));
            AddOrReplaceEngines(GetOrFixGeneration(spark, "I (2009–2015)", "chevrolet-spark-i", 2009, 2015), 55, [
                new EngineVersion { EngineName = "1.0 68 KM", PowerHP = 68, PowerKW = 50, Displacement = 995, FuelTypeId = ben,
                    TorqueNm = 93, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 15.2m, TopSpeedKmh = 155, FuelConsumptionCombined = 5.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(spark, "II (2015–2022)", "chevrolet-spark-ii", 2015, 2022), 55, [
                new EngineVersion { EngineName = "1.0 65 KM", PowerHP = 65, PowerKW = 48, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 92, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 15.5m, TopSpeedKmh = 154, FuelConsumptionCombined = 4.9m },
                new EngineVersion { EngineName = "1.4 101 KM", PowerHP = 101, PowerKW = 74, Displacement = 1398, FuelTypeId = ben,
                    TorqueNm = 130, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.6m, TopSpeedKmh = 170, FuelConsumptionCombined = 5.8m },
            ]);

            int tahoe = GetOrCreateModel(chevId, "Tahoe", "chevrolet-tahoe");
            PrepareGenerations(tahoe,
                ("I (1995–1999)", "chevrolet-tahoe-i", 1995, 1999),
                ("III (2007–2014)", "chevrolet-tahoe-iii", 2007, 2014),
                ("V (2021–)", "chevrolet-tahoe-v", 2021, null));
            AddOrReplaceEngines(GetOrFixGeneration(tahoe, "I (1995–1999)", "chevrolet-tahoe-i", 1995, 1999), 200, [
                new EngineVersion { EngineName = "5.7 V8 255 KM", PowerHP = 255, PowerKW = 187, Displacement = 5733, FuelTypeId = ben,
                    TorqueNm = 407, EuroNorm = "Euro 2", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 9.5m, TopSpeedKmh = 170, FuelConsumptionCombined = 18.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(tahoe, "III (2007–2014)", "chevrolet-tahoe-iii", 2007, 2014), 280, [
                new EngineVersion { EngineName = "5.3 V8 320 KM", PowerHP = 320, PowerKW = 235, Displacement = 5327, FuelTypeId = ben,
                    TorqueNm = 468, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 8.5m, TopSpeedKmh = 180, FuelConsumptionCombined = 16.5m },
                new EngineVersion { EngineName = "6.2 V8 403 KM", PowerHP = 403, PowerKW = 296, Displacement = 6162, FuelTypeId = ben,
                    TorqueNm = 583, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 7.2m, TopSpeedKmh = 190, FuelConsumptionCombined = 17.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(tahoe, "V (2021–)", "chevrolet-tahoe-v", 2021, null), 280, [
                new EngineVersion { EngineName = "5.3 V8 355 KM", PowerHP = 355, PowerKW = 261, Displacement = 5328, FuelTypeId = ben,
                    TorqueNm = 519, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 7.0m, TopSpeedKmh = 190, FuelConsumptionCombined = 15.5m },
                new EngineVersion { EngineName = "6.2 V8 420 KM", PowerHP = 420, PowerKW = 309, Displacement = 6162, FuelTypeId = ben,
                    TorqueNm = 624, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 6.1m, TopSpeedKmh = 195, FuelConsumptionCombined = 16.9m },
                new EngineVersion { EngineName = "3.0 Duramax Diesel 277 KM", PowerHP = 277, PowerKW = 204, Displacement = 2993, FuelTypeId = die,
                    TorqueNm = 623, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 8.0m, TopSpeedKmh = 180, FuelConsumptionCombined = 9.8m },
            ]);

            int traverse = GetOrCreateModel(chevId, "Traverse", "chevrolet-traverse");
            PrepareGenerations(traverse,
                ("I (2009–2017)", "chevrolet-traverse-i", 2009, 2017),
                ("II (2017–)", "chevrolet-traverse-ii", 2017, null));
            AddOrReplaceEngines(GetOrFixGeneration(traverse, "I (2009–2017)", "chevrolet-traverse-i", 2009, 2017), 200, [
                new EngineVersion { EngineName = "3.6 V6 288 KM", PowerHP = 288, PowerKW = 212, Displacement = 3564, FuelTypeId = ben,
                    TorqueNm = 360, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 8.0m, TopSpeedKmh = 190, FuelConsumptionCombined = 12.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(traverse, "II (2017–)", "chevrolet-traverse-ii", 2017, null), 200, [
                new EngineVersion { EngineName = "3.6 V6 314 KM", PowerHP = 314, PowerKW = 231, Displacement = 3649, FuelTypeId = ben,
                    TorqueNm = 373, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 7.4m, TopSpeedKmh = 200, FuelConsumptionCombined = 12.2m },
                new EngineVersion { EngineName = "2.0T 228 KM", PowerHP = 228, PowerKW = 168, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.6m, TopSpeedKmh = 195, FuelConsumptionCombined = 10.5m },
            ]);

            int trax = GetOrCreateModel(chevId, "Trax", "chevrolet-trax");
            PrepareGenerations(trax,
                ("I (2013–2016)", "chevrolet-trax-i", 2013, 2016),
                ("II (2023–)", "chevrolet-trax-ii", 2023, null));
            AddOrReplaceEngines(GetOrFixGeneration(trax, "I (2013–2016)", "chevrolet-trax-i", 2013, 2016), 100, [
                new EngineVersion { EngineName = "1.4T 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1364, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.7m, TopSpeedKmh = 187, FuelConsumptionCombined = 6.8m },
                new EngineVersion { EngineName = "1.6 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 155, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.7m, TopSpeedKmh = 172, FuelConsumptionCombined = 7.2m },
                new EngineVersion { EngineName = "1.7D 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1686, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 10.1m, TopSpeedKmh = 180, FuelConsumptionCombined = 5.2m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(trax, "II (2023–)", "chevrolet-trax-ii", 2023, null), 100, [
                new EngineVersion { EngineName = "1.2T 137 KM", PowerHP = 137, PowerKW = 101, Displacement = 1199, FuelTypeId = ben,
                    TorqueNm = 220, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.5m, TopSpeedKmh = 190, FuelConsumptionCombined = 6.8m },
            ]);
        }

        // ── DAF Trucks ────────────────────────────────────────────────────────────
        {
            int dafId = GetOrCreateBrand("DAF", "daf", "ciezarowe");
            int xf105 = GetOrCreateModel(dafId, "XF 105", "daf-xf105");
            AddEngines(GetOrCreateGeneration(xf105, "105 (2005–2017)", "daf-xf-105", 2005, 2017), [
                new EngineVersion { EngineName = "MX-13 410 KM Euro 5", PowerHP = 410, PowerKW = 301, Displacement = 12902, FuelTypeId = die,
                    TorqueNm = 2000, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "MX-13 460 KM Euro 6", PowerHP = 460, PowerKW = 338, Displacement = 12902, FuelTypeId = die,
                    TorqueNm = 2300, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "MX-13 510 KM Euro 6", PowerHP = 510, PowerKW = 375, Displacement = 12902, FuelTypeId = die,
                    TorqueNm = 2500, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
            ]);
            int xf2017 = GetOrCreateModel(dafId, "XF", "daf-xf");
            AddEngines(GetOrCreateGeneration(xf2017, "XF (2017–)", "daf-xf-2017", 2017, null), [
                new EngineVersion { EngineName = "MX-11 390 KM", PowerHP = 390, PowerKW = 287, Displacement = 10837, FuelTypeId = die,
                    TorqueNm = 1950, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "MX-13 430 KM", PowerHP = 430, PowerKW = 316, Displacement = 12902, FuelTypeId = die,
                    TorqueNm = 2100, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "MX-13 530 KM", PowerHP = 530, PowerKW = 390, Displacement = 12902, FuelTypeId = die,
                    TorqueNm = 2600, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
            ]);
            int xg = GetOrCreateModel(dafId, "XG/XG+", "daf-xg");
            AddEngines(GetOrCreateGeneration(xg, "XG/XG+ (2021–)", "daf-xg-2021", 2021, null), [
                new EngineVersion { EngineName = "MX-13 480 KM", PowerHP = 480, PowerKW = 353, Displacement = 12902, FuelTypeId = die,
                    TorqueNm = 2500, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "MX-13 530 KM", PowerHP = 530, PowerKW = 390, Displacement = 12902, FuelTypeId = die,
                    TorqueNm = 2600, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "MX-13 570 KM", PowerHP = 570, PowerKW = 419, Displacement = 12902, FuelTypeId = die,
                    TorqueNm = 2750, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
            ]);
            int lf = GetOrCreateModel(dafId, "LF", "daf-lf");
            AddEngines(GetOrCreateGeneration(lf, "FA/FAR (2013–)", "daf-lf-2013", 2013, null), [
                new EngineVersion { EngineName = "PX-5 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 4485, FuelTypeId = die,
                    TorqueNm = 750, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 5, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "PX-7 290 KM", PowerHP = 290, PowerKW = 213, Displacement = 6728, FuelTypeId = die,
                    TorqueNm = 1150, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
            ]);
        }

        // ── Iveco Trucks ──────────────────────────────────────────────────────────
        {
            int iveId = GetOrCreateBrand("Iveco", "iveco", "ciezarowe", "dostawcze");
            int sway = GetOrCreateModel(iveId, "S-Way", "iveco-s-way");
            AddEngines(GetOrCreateGeneration(sway, "S-Way (2019–)", "iveco-s-way-2019", 2019, null), [
                new EngineVersion { EngineName = "Cursor 11 430 KM", PowerHP = 430, PowerKW = 316, Displacement = 10308, FuelTypeId = die,
                    TorqueNm = 2000, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Cursor 13 480 KM", PowerHP = 480, PowerKW = 353, Displacement = 12882, FuelTypeId = die,
                    TorqueNm = 2300, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Cursor 13 570 KM", PowerHP = 570, PowerKW = 419, Displacement = 12882, FuelTypeId = die,
                    TorqueNm = 2700, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
            ]);
            int stralis = GetOrCreateModel(iveId, "Stralis", "iveco-stralis");
            AddEngines(GetOrCreateGeneration(stralis, "Hi-Way (2012–2019)", "iveco-stralis-hiway", 2012, 2019), [
                new EngineVersion { EngineName = "Cursor 11 410 KM", PowerHP = 410, PowerKW = 301, Displacement = 10308, FuelTypeId = die,
                    TorqueNm = 1900, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Cursor 13 460 KM", PowerHP = 460, PowerKW = 338, Displacement = 12882, FuelTypeId = die,
                    TorqueNm = 2200, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "Cursor 13 560 KM", PowerHP = 560, PowerKW = 412, Displacement = 12882, FuelTypeId = die,
                    TorqueNm = 2600, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
            ]);
            int daily = GetOrCreateModel(iveId, "Daily", "iveco-daily");
            AddEngines(GetOrCreateGeneration(daily, "VI (2014–)", "iveco-daily-vi", 2014, null), [
                new EngineVersion { EngineName = "2.3 HPI 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 2287, FuelTypeId = die,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = 140, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "3.0 HPI 160 KM", PowerHP = 160, PowerKW = 118, Displacement = 2998, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 145, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "3.0 HPI 210 KM", PowerHP = 210, PowerKW = 154, Displacement = 2998, FuelTypeId = die,
                    TorqueNm = 470, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 150, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "eDaily EV 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "RWD",
                    Cylinders = 0, Acceleration0100 = null, TopSpeedKmh = 100, FuelConsumptionCombined = null },
            ]);
        }

        // ── Renault Trucks ────────────────────────────────────────────────────────
        {
            int rtId = GetOrCreateBrand("Renault Trucks", "renault-trucks", "ciezarowe");
            int tHigh = GetOrCreateModel(rtId, "T High", "renault-trucks-t");
            AddEngines(GetOrCreateGeneration(tHigh, "T High (2013–)", "rt-t-high-2013", 2013, null), [
                new EngineVersion { EngineName = "DTI 11 430 KM", PowerHP = 430, PowerKW = 316, Displacement = 10837, FuelTypeId = die,
                    TorqueNm = 2100, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "DTI 13 480 KM", PowerHP = 480, PowerKW = 353, Displacement = 12777, FuelTypeId = die,
                    TorqueNm = 2400, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "DTI 13 520 KM", PowerHP = 520, PowerKW = 382, Displacement = 12777, FuelTypeId = die,
                    TorqueNm = 2550, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
            ]);
            int rC = GetOrCreateModel(rtId, "C", "renault-trucks-c");
            AddEngines(GetOrCreateGeneration(rC, "C (2013–)", "rt-c-2013", 2013, null), [
                new EngineVersion { EngineName = "DTI 8 280 KM", PowerHP = 280, PowerKW = 206, Displacement = 7698, FuelTypeId = die,
                    TorqueNm = 1150, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "DTI 11 380 KM", PowerHP = 380, PowerKW = 279, Displacement = 10837, FuelTypeId = die,
                    TorqueNm = 1900, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
            ]);
            int rD = GetOrCreateModel(rtId, "D Wide", "renault-trucks-d");
            AddEngines(GetOrCreateGeneration(rD, "D Wide (2013–)", "rt-d-wide-2013", 2013, null), [
                new EngineVersion { EngineName = "DTI 5 170 KM", PowerHP = 170, PowerKW = 125, Displacement = 5136, FuelTypeId = die,
                    TorqueNm = 750, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "DTI 8 280 KM", PowerHP = 280, PowerKW = 206, Displacement = 7698, FuelTypeId = die,
                    TorqueNm = 1150, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 90, FuelConsumptionCombined = null },
            ]);
        }

        // ── Kubota Agricultural ───────────────────────────────────────────────────
        {
            int kubId = GetOrCreateBrand("Kubota", "kubota", "rolnicze");
            int m5 = GetOrCreateModel(kubId, "M5", "kubota-m5");
            AddEngines(GetOrCreateGeneration(m5, "M5-091 (2015–)", "kubota-m5-2015", 2015, null), [
                new EngineVersion { EngineName = "4.335L 4-cyl 99 KM Stage V", PowerHP = 99, PowerKW = 73, Displacement = 4335, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Stage V", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = 40, FuelConsumptionCombined = null },
            ]);
            int lSeries = GetOrCreateModel(kubId, "L Series", "kubota-l-series");
            AddEngines(GetOrCreateGeneration(lSeries, "L2502 (2016–)", "kubota-l2502-2016", 2016, null), [
                new EngineVersion { EngineName = "1.826L 3-cyl 24.5 KM Stage V", PowerHP = 25, PowerKW = 18, Displacement = 1826, FuelTypeId = die,
                    TorqueNm = 85, EuroNorm = "Stage V", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 3, Acceleration0100 = null, TopSpeedKmh = 25, FuelConsumptionCombined = null },
            ]);
        }

        // ── Caterpillar Construction ──────────────────────────────────────────────
        {
            int catId = GetOrCreateBrand("Caterpillar", "caterpillar", "budowlane");
            int cat320 = GetOrCreateModel(catId, "CAT 320", "cat-320");
            AddEngines(GetOrCreateGeneration(cat320, "CAT 320 (2019–)", "cat-320-2019", 2019, null), [
                new EngineVersion { EngineName = "C4.4 diesel 121 KM Stage V", PowerHP = 121, PowerKW = 89, Displacement = 4400, FuelTypeId = die,
                    TorqueNm = 700, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
            int cat950 = GetOrCreateModel(catId, "CAT 950", "cat-950");
            AddEngines(GetOrCreateGeneration(cat950, "GC/M (2015–)", "cat-950-2015", 2015, null), [
                new EngineVersion { EngineName = "C7.1 diesel 230 KM Stage V", PowerHP = 230, PowerKW = 169, Displacement = 7100, FuelTypeId = die,
                    TorqueNm = 1050, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
            int catD6 = GetOrCreateModel(catId, "CAT D6", "cat-d6");
            AddEngines(GetOrCreateGeneration(catD6, "XE/XL (2016–)", "cat-d6-2016", 2016, null), [
                new EngineVersion { EngineName = "C9.3B diesel 218 KM Stage V", PowerHP = 218, PowerKW = 160, Displacement = 9300, FuelTypeId = die,
                    TorqueNm = 1100, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
            int cat432 = GetOrCreateModel(catId, "CAT 432", "cat-432");
            AddEngines(GetOrCreateGeneration(cat432, "F2 (2015–)", "cat-432-f2", 2015, null), [
                new EngineVersion { EngineName = "C3.4B diesel 74 KM Stage V", PowerHP = 74, PowerKW = 55, Displacement = 3400, FuelTypeId = die,
                    TorqueNm = 350, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
        }

        // ── JCB Construction ──────────────────────────────────────────────────────
        {
            int jcbId = GetOrCreateBrand("JCB", "jcb", "budowlane");
            int jcb3cx = GetOrCreateModel(jcbId, "3CX", "jcb-3cx");
            AddEngines(GetOrCreateGeneration(jcb3cx, "4T4 (2013–)", "jcb-3cx-4t4", 2013, null), [
                new EngineVersion { EngineName = "JCB EcoMAX 4-cyl 74 KM Stage V", PowerHP = 74, PowerKW = 55, Displacement = 4400, FuelTypeId = die,
                    TorqueNm = 340, EuroNorm = "Stage V", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "JCB EcoMAX 4-cyl 109 KM Stage V", PowerHP = 109, PowerKW = 80, Displacement = 4400, FuelTypeId = die,
                    TorqueNm = 430, EuroNorm = "Stage V", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
            int js220 = GetOrCreateModel(jcbId, "JS220", "jcb-js220");
            AddEngines(GetOrCreateGeneration(js220, "LC (2014–)", "jcb-js220-lc", 2014, null), [
                new EngineVersion { EngineName = "JCB DieselMAX 6-cyl 156 KM Stage V", PowerHP = 156, PowerKW = 115, Displacement = 6700, FuelTypeId = die,
                    TorqueNm = 780, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
            int j52560 = GetOrCreateModel(jcbId, "525-60", "jcb-525-60");
            AddEngines(GetOrCreateGeneration(j52560, "T4 (2015–)", "jcb-525-60-t4", 2015, null), [
                new EngineVersion { EngineName = "JCB 3-cyl diesel 55 KM Stage V", PowerHP = 55, PowerKW = 40, Displacement = 2200, FuelTypeId = die,
                    TorqueNm = 200, EuroNorm = "Stage V", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 3, Acceleration0100 = null, TopSpeedKmh = 30, FuelConsumptionCombined = null },
            ]);
            int fastrac = GetOrCreateModel(jcbId, "Fastrac 4000", "jcb-fastrac-4000");
            AddEngines(GetOrCreateGeneration(fastrac, "4220 (2016–)", "jcb-fastrac-4220", 2016, null), [
                new EngineVersion { EngineName = "AGCO Power 6-cyl 205 KM Stage V", PowerHP = 205, PowerKW = 151, Displacement = 6600, FuelTypeId = die,
                    TorqueNm = 860, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 70, FuelConsumptionCombined = null },
            ]);
        }

        // ── Komatsu Construction ──────────────────────────────────────────────────
        {
            int komId = GetOrCreateBrand("Komatsu", "komatsu", "budowlane");
            int pc210 = GetOrCreateModel(komId, "PC210", "komatsu-pc210");
            AddEngines(GetOrCreateGeneration(pc210, "LC-11 (2017–)", "komatsu-pc210-lc11", 2017, null), [
                new EngineVersion { EngineName = "SAA6D107E-3 6-cyl 165 KM Stage V", PowerHP = 165, PowerKW = 121, Displacement = 6690, FuelTypeId = die,
                    TorqueNm = 800, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
            int wa320 = GetOrCreateModel(komId, "WA320", "komatsu-wa320");
            AddEngines(GetOrCreateGeneration(wa320, "8 (2019–)", "komatsu-wa320-8", 2019, null), [
                new EngineVersion { EngineName = "SAA6D107E 6-cyl 201 KM Stage V", PowerHP = 201, PowerKW = 148, Displacement = 6690, FuelTypeId = die,
                    TorqueNm = 850, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
            int d65 = GetOrCreateModel(komId, "D65", "komatsu-d65");
            AddEngines(GetOrCreateGeneration(d65, "PX-18 (2018–)", "komatsu-d65-px18", 2018, null), [
                new EngineVersion { EngineName = "SAA6D114E-6 6-cyl 235 KM Stage V", PowerHP = 235, PowerKW = 173, Displacement = 8270, FuelTypeId = die,
                    TorqueNm = 1100, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
        }

        // ── Liebherr Construction ─────────────────────────────────────────────────
        {
            int liebId = GetOrCreateBrand("Liebherr", "liebherr", "budowlane");
            int ltm1060 = GetOrCreateModel(liebId, "LTM 1060", "liebherr-ltm1060");
            AddEngines(GetOrCreateGeneration(ltm1060, "4.2 (2018–)", "liebherr-ltm1060-4-2", 2018, null), [
                new EngineVersion { EngineName = "Liebherr D946 diesel 510 KM Stage V", PowerHP = 510, PowerKW = 375, Displacement = 15600, FuelTypeId = die,
                    TorqueNm = 2700, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = null, TopSpeedKmh = 75, FuelConsumptionCombined = null },
            ]);
            int pr736 = GetOrCreateModel(liebId, "PR 736", "liebherr-pr736");
            AddEngines(GetOrCreateGeneration(pr736, "Litronic (2017–)", "liebherr-pr736-litronic", 2017, null), [
                new EngineVersion { EngineName = "Liebherr D936 diesel 180 KM Stage V", PowerHP = 180, PowerKW = 132, Displacement = 6600, FuelTypeId = die,
                    TorqueNm = 900, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
            int lb28 = GetOrCreateModel(liebId, "LB 28", "liebherr-lb28");
            AddEngines(GetOrCreateGeneration(lb28, "LB28 (2014–)", "liebherr-lb28-2014", 2014, null), [
                new EngineVersion { EngineName = "Liebherr D914 diesel 120 KM Stage V", PowerHP = 120, PowerKW = 88, Displacement = 4500, FuelTypeId = die,
                    TorqueNm = 600, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
        }

        // ── Bobcat Construction ───────────────────────────────────────────────────
        {
            int bobId = GetOrCreateBrand("Bobcat", "bobcat", "budowlane");
            int e85 = GetOrCreateModel(bobId, "E85", "bobcat-e85");
            AddEngines(GetOrCreateGeneration(e85, "E85 (2016–)", "bobcat-e85-2016", 2016, null), [
                new EngineVersion { EngineName = "Bobcat 4-cyl diesel 57 KM Stage V", PowerHP = 57, PowerKW = 42, Displacement = 2800, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
            int t650 = GetOrCreateModel(bobId, "T650", "bobcat-t650");
            AddEngines(GetOrCreateGeneration(t650, "T650 (2014–)", "bobcat-t650-2014", 2014, null), [
                new EngineVersion { EngineName = "Bobcat V3300 diesel 68 KM Stage V", PowerHP = 68, PowerKW = 50, Displacement = 3300, FuelTypeId = die,
                    TorqueNm = 280, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
            int s850 = GetOrCreateModel(bobId, "S850", "bobcat-s850");
            AddEngines(GetOrCreateGeneration(s850, "S850 (2019–)", "bobcat-s850-2019", 2019, null), [
                new EngineVersion { EngineName = "Bobcat 4-cyl diesel 103 KM Stage V", PowerHP = 103, PowerKW = 76, Displacement = 3400, FuelTypeId = die,
                    TorqueNm = 430, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
        }

        // ── Takeuchi Construction ─────────────────────────────────────────────────
        {
            int takId = GetOrCreateBrand("Takeuchi", "takeuchi", "budowlane");
            int tb216 = GetOrCreateModel(takId, "TB216", "takeuchi-tb216");
            AddEngines(GetOrCreateGeneration(tb216, "TB216 (2020–)", "takeuchi-tb216-2020", 2020, null), [
                new EngineVersion { EngineName = "Yanmar 4TNV84T 16 KM Stage V", PowerHP = 16, PowerKW = 12, Displacement = 1005, FuelTypeId = die,
                    TorqueNm = 65, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
            int tb260 = GetOrCreateModel(takId, "TB260", "takeuchi-tb260");
            AddEngines(GetOrCreateGeneration(tb260, "TB260 (2017–)", "takeuchi-tb260-2017", 2017, null), [
                new EngineVersion { EngineName = "Kubota V3307 4-cyl 51 KM Stage V", PowerHP = 51, PowerKW = 37, Displacement = 3331, FuelTypeId = die,
                    TorqueNm = 210, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
        }

        // ── Wacker Neuson Construction ────────────────────────────────────────────
        {
            int wkId = GetOrCreateBrand("Wacker Neuson", "wacker-neuson", "budowlane");
            int ew100 = GetOrCreateModel(wkId, "EW100", "wacker-neuson-ew100");
            AddEngines(GetOrCreateGeneration(ew100, "EW100 (2017–)", "wacker-neuson-ew100-2017", 2017, null), [
                new EngineVersion { EngineName = "Deutz TCD 2.9 L4 74 KM Stage V", PowerHP = 74, PowerKW = 55, Displacement = 2900, FuelTypeId = die,
                    TorqueNm = 310, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
            int ez80 = GetOrCreateModel(wkId, "EZ80", "wacker-neuson-ez80");
            AddEngines(GetOrCreateGeneration(ez80, "EZ80 (2017–)", "wacker-neuson-ez80-2017", 2017, null), [
                new EngineVersion { EngineName = "Yanmar 4TNV98C diesel 57 KM Stage V", PowerHP = 57, PowerKW = 42, Displacement = 3318, FuelTypeId = die,
                    TorqueNm = 255, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
        }

        // ── Doosan Construction ───────────────────────────────────────────────────
        {
            int dosId = GetOrCreateBrand("Doosan", "doosan", "budowlane");
            int dx225 = GetOrCreateModel(dosId, "DX225LC", "doosan-dx225lc");
            AddEngines(GetOrCreateGeneration(dx225, "DX225LC-5 (2014–)", "doosan-dx225lc-5", 2014, null), [
                new EngineVersion { EngineName = "DL06 6-cyl 168 KM Stage V", PowerHP = 168, PowerKW = 123, Displacement = 5890, FuelTypeId = die,
                    TorqueNm = 850, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
            int dx530 = GetOrCreateModel(dosId, "DX530LC", "doosan-dx530lc");
            AddEngines(GetOrCreateGeneration(dx530, "DX530LC-5 (2015–)", "doosan-dx530lc-5", 2015, null), [
                new EngineVersion { EngineName = "DL08 6-cyl 388 KM Stage V", PowerHP = 388, PowerKW = 285, Displacement = 7582, FuelTypeId = die,
                    TorqueNm = 1900, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
        }

        // ── Hitachi Construction ──────────────────────────────────────────────────
        {
            int hitId = GetOrCreateBrand("Hitachi", "hitachi", "budowlane");
            int zx210 = GetOrCreateModel(hitId, "ZX210LC", "hitachi-zx210lc");
            AddEngines(GetOrCreateGeneration(zx210, "ZX210LC-6 (2014–)", "hitachi-zx210lc-6", 2014, null), [
                new EngineVersion { EngineName = "Isuzu 4HK1X diesel 158 KM Stage V", PowerHP = 158, PowerKW = 116, Displacement = 5193, FuelTypeId = die,
                    TorqueNm = 750, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
            int zx470 = GetOrCreateModel(hitId, "ZX470LC", "hitachi-zx470lc");
            AddEngines(GetOrCreateGeneration(zx470, "ZX470LC-6 (2015–)", "hitachi-zx470lc-6", 2015, null), [
                new EngineVersion { EngineName = "Isuzu 6HK1X diesel 358 KM Stage V", PowerHP = 358, PowerKW = 263, Displacement = 7790, FuelTypeId = die,
                    TorqueNm = 1700, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
        }

        // ── Terex Construction ────────────────────────────────────────────────────
        {
            int terexId = GetOrCreateBrand("Terex", "terex", "budowlane");
            int tc50 = GetOrCreateModel(terexId, "TC50", "terex-tc50");
            AddEngines(GetOrCreateGeneration(tc50, "TC50 (2014–)", "terex-tc50-2014", 2014, null), [
                new EngineVersion { EngineName = "Yanmar 4TNV94 diesel 41 KM Stage V", PowerHP = 41, PowerKW = 30, Displacement = 3318, FuelTypeId = die,
                    TorqueNm = 175, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = null, TopSpeedKmh = null, FuelConsumptionCombined = null },
            ]);
            int ac100 = GetOrCreateModel(terexId, "AC 100-4", "terex-ac100-4");
            AddEngines(GetOrCreateGeneration(ac100, "AC 100-4 (2016–)", "terex-ac100-4-2016", 2016, null), [
                new EngineVersion { EngineName = "Mercedes OM 471 diesel 503 KM Stage V", PowerHP = 503, PowerKW = 370, Displacement = 12800, FuelTypeId = die,
                    TorqueNm = 2500, EuroNorm = "Stage V", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = null, TopSpeedKmh = 75, FuelConsumptionCombined = null },
            ]);
        }


        // ── VW GOLF/TIGUAN/TOUAREG (older & missing generations) ─────────────────
        {
            int bId = GetOrCreateBrand("Volkswagen", "volkswagen", "auta-osobowe");

            int golf = GetOrCreateModel(bId, "Golf", "vw-golf");
            AddOrReplaceEngines(GetOrFixGeneration(golf, "Mk4 (1997–2003)", "vw-golf-mk4", 1997, 2003), 60, [
                new EngineVersion { EngineName = "1.4 16V 75 KM", PowerHP = 75, PowerKW = 55, Displacement = 1390, FuelTypeId = ben,
                    TorqueNm = 126, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 14.3m, TopSpeedKmh = 166, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "1.6 102 KM", PowerHP = 102, PowerKW = 75, Displacement = 1595, FuelTypeId = ben,
                    TorqueNm = 148, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.3m, TopSpeedKmh = 182, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "2.0 GTI 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 172, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.2m, TopSpeedKmh = 196, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "1.9 TDI 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1896, FuelTypeId = die,
                    TorqueNm = 202, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.1m, TopSpeedKmh = 178, FuelConsumptionCombined = 5.2m },
                new EngineVersion { EngineName = "1.9 TDI 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1896, FuelTypeId = die,
                    TorqueNm = 285, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 195, FuelConsumptionCombined = 5.6m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(golf, "Mk5 (2003–2008)", "vw-golf-mk5", 2003, 2008), 70, [
                new EngineVersion { EngineName = "1.4 80 KM", PowerHP = 80, PowerKW = 59, Displacement = 1390, FuelTypeId = ben,
                    TorqueNm = 132, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.6m, TopSpeedKmh = 170, FuelConsumptionCombined = 6.9m },
                new EngineVersion { EngineName = "1.6 FSI 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 155, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.9m, TopSpeedKmh = 195, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "2.0 GTI 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 280, EuroNorm = "Euro 4", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.2m, TopSpeedKmh = 233, FuelConsumptionCombined = 8.6m },
                new EngineVersion { EngineName = "R32 250 KM", PowerHP = 250, PowerKW = 184, Displacement = 3189, FuelTypeId = ben,
                    TorqueNm = 320, EuroNorm = "Euro 4", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.5m, TopSpeedKmh = 250, FuelConsumptionCombined = 11.0m },
                new EngineVersion { EngineName = "1.9 TDI 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 1896, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.3m, TopSpeedKmh = 188, FuelConsumptionCombined = 4.9m },
                new EngineVersion { EngineName = "2.0 TDI 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 4", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.3m, TopSpeedKmh = 205, FuelConsumptionCombined = 5.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(golf, "Mk6 (2008–2012)", "vw-golf-mk6", 2008, 2012), 80, [
                new EngineVersion { EngineName = "1.2 TSI 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 1197, FuelTypeId = ben,
                    TorqueNm = 175, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.9m, TopSpeedKmh = 188, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "1.4 TSI 122 KM", PowerHP = 122, PowerKW = 90, Displacement = 1390, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.5m, TopSpeedKmh = 199, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "2.0 GTI 210 KM", PowerHP = 210, PowerKW = 155, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 280, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.9m, TopSpeedKmh = 238, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "R 270 KM", PowerHP = 270, PowerKW = 199, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 5.7m, TopSpeedKmh = 250, FuelConsumptionCombined = 9.0m },
                new EngineVersion { EngineName = "1.6 TDI 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.3m, TopSpeedKmh = 190, FuelConsumptionCombined = 4.5m },
                new EngineVersion { EngineName = "2.0 TDI 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.8m, TopSpeedKmh = 210, FuelConsumptionCombined = 4.9m },
            ]);

            int tiguan = GetOrCreateModel(bId, "Tiguan", "vw-tiguan");
            AddOrReplaceEngines(GetOrFixGeneration(tiguan, "I (2007–2016)", "vw-tiguan-i", 2007, 2016), 100, [
                new EngineVersion { EngineName = "1.4 TSI 122 KM", PowerHP = 122, PowerKW = 90, Displacement = 1390, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.4m, TopSpeedKmh = 178, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "2.0 TSI 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 280, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.1m, TopSpeedKmh = 205, FuelConsumptionCombined = 8.8m },
                new EngineVersion { EngineName = "2.0 TDI 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.1m, TopSpeedKmh = 190, FuelConsumptionCombined = 5.8m },
                new EngineVersion { EngineName = "2.0 TDI 4Motion 177 KM", PowerHP = 177, PowerKW = 130, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.6m, TopSpeedKmh = 205, FuelConsumptionCombined = 6.4m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(tiguan, "II (2016–2023)", "vw-tiguan-ii", 2016, 2023), 110, [
                new EngineVersion { EngineName = "1.5 TSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1498, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.2m, TopSpeedKmh = 199, FuelConsumptionCombined = 6.8m },
                new EngineVersion { EngineName = "2.0 TSI 4Motion 220 KM", PowerHP = 220, PowerKW = 162, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.9m, TopSpeedKmh = 219, FuelConsumptionCombined = 8.2m },
                new EngineVersion { EngineName = "2.0 TDI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 340, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.3m, TopSpeedKmh = 205, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "2.0 TDI 4Motion 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.9m, TopSpeedKmh = 213, FuelConsumptionCombined = 6.3m },
                new EngineVersion { EngineName = "eHybrid PHEV 245 KM", PowerHP = 245, PowerKW = 180, Displacement = 1395, FuelTypeId = phev,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.4m, TopSpeedKmh = 205, FuelConsumptionCombined = 1.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(tiguan, "III (2024–)", "vw-tiguan-iii", 2024, null), 110, [
                new EngineVersion { EngineName = "1.5 eTSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1498, FuelTypeId = mild,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.2m, TopSpeedKmh = 205, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "2.0 TSI 4Motion 265 KM", PowerHP = 265, PowerKW = 195, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.2m, TopSpeedKmh = 222, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "2.0 TDI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 360, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.3m, TopSpeedKmh = 206, FuelConsumptionCombined = 5.1m },
                new EngineVersion { EngineName = "eHybrid PHEV 272 KM", PowerHP = 272, PowerKW = 200, Displacement = 1498, FuelTypeId = phev,
                    TorqueNm = 380, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.6m, TopSpeedKmh = 210, FuelConsumptionCombined = 1.2m },
            ]);

            int touareg = GetOrCreateModel(bId, "Touareg", "vw-touareg");
            AddOrReplaceEngines(GetOrFixGeneration(touareg, "I (2002–2010)", "vw-touareg-i", 2002, 2010), 130, [
                new EngineVersion { EngineName = "3.2 V6 220 KM", PowerHP = 220, PowerKW = 162, Displacement = 3189, FuelTypeId = ben,
                    TorqueNm = 315, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 9.8m, TopSpeedKmh = 197, FuelConsumptionCombined = 12.9m },
                new EngineVersion { EngineName = "4.2 V8 310 KM", PowerHP = 310, PowerKW = 228, Displacement = 4172, FuelTypeId = ben,
                    TorqueNm = 420, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 7.8m, TopSpeedKmh = 216, FuelConsumptionCombined = 14.9m },
                new EngineVersion { EngineName = "2.5 TDI 174 KM", PowerHP = 174, PowerKW = 128, Displacement = 2461, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "AWD",
                    Cylinders = 5, Acceleration0100 = 12.4m, TopSpeedKmh = 179, FuelConsumptionCombined = 9.9m },
                new EngineVersion { EngineName = "3.0 V6 TDI 240 KM", PowerHP = 240, PowerKW = 176, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 500, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 8.6m, TopSpeedKmh = 210, FuelConsumptionCombined = 9.9m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(touareg, "II (2010–2018)", "vw-touareg-ii", 2010, 2018), 150, [
                new EngineVersion { EngineName = "3.6 V6 FSI 280 KM", PowerHP = 280, PowerKW = 206, Displacement = 3597, FuelTypeId = ben,
                    TorqueNm = 360, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 7.8m, TopSpeedKmh = 219, FuelConsumptionCombined = 11.0m },
                new EngineVersion { EngineName = "3.0 V6 TDI 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 450, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 8.3m, TopSpeedKmh = 202, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "3.0 V6 TDI 262 KM", PowerHP = 262, PowerKW = 193, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 580, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.6m, TopSpeedKmh = 224, FuelConsumptionCombined = 7.8m },
                new EngineVersion { EngineName = "V8 TDI 340 KM", PowerHP = 340, PowerKW = 250, Displacement = 4134, FuelTypeId = die,
                    TorqueNm = 800, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 5.8m, TopSpeedKmh = 250, FuelConsumptionCombined = 9.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(touareg, "III (2018–)", "vw-touareg-iii", 2018, null), 170, [
                new EngineVersion { EngineName = "3.0 V6 TSI 340 KM", PowerHP = 340, PowerKW = 250, Displacement = 2995, FuelTypeId = mild,
                    TorqueNm = 450, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.1m, TopSpeedKmh = 240, FuelConsumptionCombined = 10.5m },
                new EngineVersion { EngineName = "3.0 V6 TDI 231 KM", PowerHP = 231, PowerKW = 170, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 7.9m, TopSpeedKmh = 210, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "3.0 V6 TDI 286 KM", PowerHP = 286, PowerKW = 210, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 600, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.1m, TopSpeedKmh = 235, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "eHybrid PHEV 462 KM", PowerHP = 462, PowerKW = 340, Displacement = 2995, FuelTypeId = phev,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.0m, TopSpeedKmh = 253, FuelConsumptionCombined = 2.7m },
            ]);

            int tRoc = GetOrCreateModel(bId, "T-Roc", "vw-t-roc");
            AddOrReplaceEngines(GetOrFixGeneration(tRoc, "A11 (2017–)", "vw-t-roc-a11", 2017, null), 90, [
                new EngineVersion { EngineName = "1.0 TSI 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.8m, TopSpeedKmh = 187, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "1.5 TSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1498, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.4m, TopSpeedKmh = 204, FuelConsumptionCombined = 6.4m },
                new EngineVersion { EngineName = "R 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 4.9m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "2.0 TDI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 340, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.4m, TopSpeedKmh = 205, FuelConsumptionCombined = 5.2m },
            ]);

            int tCross = GetOrCreateModel(bId, "T-Cross", "vw-t-cross");
            AddOrReplaceEngines(GetOrFixGeneration(tCross, "C11 (2018–)", "vw-t-cross-c11", 2018, null), 80, [
                new EngineVersion { EngineName = "1.0 TSI 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 175, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.5m, TopSpeedKmh = 175, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "1.0 TSI 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.2m, TopSpeedKmh = 192, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "1.5 TSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1498, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.4m, TopSpeedKmh = 204, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "1.6 TDI 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 180, FuelConsumptionCombined = 4.4m },
            ]);

            int touran = GetOrCreateModel(bId, "Touran", "vw-touran");
            AddOrReplaceEngines(GetOrFixGeneration(touran, "I (2003–2015)", "vw-touran-i", 2003, 2015), 100, [
                new EngineVersion { EngineName = "1.6 FSI 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 155, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.0m, TopSpeedKmh = 186, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "1.4 TSI 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1390, FuelTypeId = ben,
                    TorqueNm = 220, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 196, FuelConsumptionCombined = 6.7m },
                new EngineVersion { EngineName = "1.9 TDI 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 1896, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.7m, TopSpeedKmh = 180, FuelConsumptionCombined = 5.7m },
                new EngineVersion { EngineName = "2.0 TDI 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 198, FuelConsumptionCombined = 5.6m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(touran, "II (2015–)", "vw-touran-ii", 2015, null), 110, [
                new EngineVersion { EngineName = "1.2 TSI 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 1197, FuelTypeId = ben,
                    TorqueNm = 175, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.5m, TopSpeedKmh = 182, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "1.5 TSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1498, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.1m, TopSpeedKmh = 202, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "2.0 TDI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 340, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.0m, TopSpeedKmh = 205, FuelConsumptionCombined = 4.8m },
            ]);

            int sharan = GetOrCreateModel(bId, "Sharan", "vw-sharan");
            AddOrReplaceEngines(GetOrFixGeneration(sharan, "II (2010–)", "vw-sharan-ii", 2010, null), 130, [
                new EngineVersion { EngineName = "1.4 TSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1390, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 195, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "2.0 TDI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 340, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.2m, TopSpeedKmh = 199, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "2.0 TDI 4Motion 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.8m, TopSpeedKmh = 210, FuelConsumptionCombined = 6.3m },
            ]);

            int scirocco = GetOrCreateModel(bId, "Scirocco", "vw-scirocco");
            AddOrReplaceEngines(GetOrFixGeneration(scirocco, "III (2008–2017)", "vw-scirocco-iii", 2008, 2017), 90, [
                new EngineVersion { EngineName = "1.4 TSI 122 KM", PowerHP = 122, PowerKW = 90, Displacement = 1390, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 202, FuelConsumptionCombined = 6.4m },
                new EngineVersion { EngineName = "2.0 TSI 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 280, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.2m, TopSpeedKmh = 235, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "R 280 KM", PowerHP = 280, PowerKW = 206, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 380, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 5.5m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.8m },
                new EngineVersion { EngineName = "2.0 TDI 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.7m, TopSpeedKmh = 224, FuelConsumptionCombined = 5.4m },
            ]);

            int arteon = GetOrCreateModel(bId, "Arteon", "vw-arteon");
            AddOrReplaceEngines(GetOrFixGeneration(arteon, "I (2017–)", "vw-arteon-i", 2017, null), 140, [
                new EngineVersion { EngineName = "1.5 TSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1498, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.3m, TopSpeedKmh = 220, FuelConsumptionCombined = 6.6m },
                new EngineVersion { EngineName = "2.0 TSI 4Motion 280 KM", PowerHP = 280, PowerKW = 206, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 5.6m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.2m },
                new EngineVersion { EngineName = "2.0 TDI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 340, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.4m, TopSpeedKmh = 218, FuelConsumptionCombined = 5.1m },
                new EngineVersion { EngineName = "2.0 TDI 4Motion 240 KM", PowerHP = 240, PowerKW = 176, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.9m, TopSpeedKmh = 233, FuelConsumptionCombined = 6.2m },
            ]);
        }

        // ── AUDI A4 (older gens) / A5 / A6 / A7 / A8 / Q3 / Q7 / Q8 / TT ──────────
        {
            int bId = GetOrCreateBrand("Audi", "audi", "auta-osobowe");

            int a4 = GetOrCreateModel(bId, "A4", "audi-a4");
            AddOrReplaceEngines(GetOrFixGeneration(a4, "B5 (1994–2001)", "audi-a4-b5", 1994, 2001), 60, [
                new EngineVersion { EngineName = "1.6 101 KM", PowerHP = 101, PowerKW = 74, Displacement = 1595, FuelTypeId = ben,
                    TorqueNm = 145, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.1m, TopSpeedKmh = 188, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "1.8 T 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1781, FuelTypeId = ben,
                    TorqueNm = 210, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.7m, TopSpeedKmh = 218, FuelConsumptionCombined = 9.2m },
                new EngineVersion { EngineName = "2.8 V6 193 KM", PowerHP = 193, PowerKW = 142, Displacement = 2771, FuelTypeId = ben,
                    TorqueNm = 265, EuroNorm = "Euro 2", GearboxType = "automatic", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 7.9m, TopSpeedKmh = 232, FuelConsumptionCombined = 11.0m },
                new EngineVersion { EngineName = "1.9 TDI 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 1896, FuelTypeId = die,
                    TorqueNm = 235, EuroNorm = "Euro 2", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 195, FuelConsumptionCombined = 6.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(a4, "B6 (2000–2004)", "audi-a4-b6", 2000, 2004), 75, [
                new EngineVersion { EngineName = "1.6 102 KM", PowerHP = 102, PowerKW = 75, Displacement = 1595, FuelTypeId = ben,
                    TorqueNm = 148, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.2m, TopSpeedKmh = 188, FuelConsumptionCombined = 8.2m },
                new EngineVersion { EngineName = "1.8 T 163 KM", PowerHP = 163, PowerKW = 120, Displacement = 1781, FuelTypeId = ben,
                    TorqueNm = 210, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 222, FuelConsumptionCombined = 9.0m },
                new EngineVersion { EngineName = "3.0 V6 220 KM", PowerHP = 220, PowerKW = 162, Displacement = 2976, FuelTypeId = ben,
                    TorqueNm = 300, EuroNorm = "Euro 3", GearboxType = "automatic", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 7.1m, TopSpeedKmh = 242, FuelConsumptionCombined = 11.5m },
                new EngineVersion { EngineName = "1.9 TDI 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1896, FuelTypeId = die,
                    TorqueNm = 310, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.6m, TopSpeedKmh = 206, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "2.5 V6 TDI 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 2496, FuelTypeId = die,
                    TorqueNm = 370, EuroNorm = "Euro 3", GearboxType = "automatic", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 8.3m, TopSpeedKmh = 226, FuelConsumptionCombined = 7.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(a4, "B7 (2004–2008)", "audi-a4-b7", 2004, 2008), 85, [
                new EngineVersion { EngineName = "1.6 102 KM", PowerHP = 102, PowerKW = 75, Displacement = 1595, FuelTypeId = ben,
                    TorqueNm = 148, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.3m, TopSpeedKmh = 187, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "2.0 TFSI 200 KM", PowerHP = 200, PowerKW = 147, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 280, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.3m, TopSpeedKmh = 240, FuelConsumptionCombined = 8.7m },
                new EngineVersion { EngineName = "RS4 4.2 V8 420 KM", PowerHP = 420, PowerKW = 309, Displacement = 4163, FuelTypeId = ben,
                    TorqueNm = 430, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "quattro",
                    Cylinders = 8, Acceleration0100 = 4.8m, TopSpeedKmh = 250, FuelConsumptionCombined = 13.0m },
                new EngineVersion { EngineName = "2.0 TDI 143 KM", PowerHP = 143, PowerKW = 105, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 208, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "3.0 V6 TDI 233 KM", PowerHP = 233, PowerKW = 171, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 500, EuroNorm = "Euro 4", GearboxType = "automatic", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 6.4m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(a4, "B8 (2008–2015)", "audi-a4-b8", 2008, 2015), 100, [
                new EngineVersion { EngineName = "1.8 TFSI 160 KM", PowerHP = 160, PowerKW = 118, Displacement = 1798, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.9m, TopSpeedKmh = 217, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "2.0 TFSI 211 KM", PowerHP = 211, PowerKW = 155, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 6.7m, TopSpeedKmh = 246, FuelConsumptionCombined = 7.8m },
                new EngineVersion { EngineName = "S4 3.0 TFSI 333 KM", PowerHP = 333, PowerKW = 245, Displacement = 2995, FuelTypeId = ben,
                    TorqueNm = 440, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 5.1m, TopSpeedKmh = 250, FuelConsumptionCombined = 9.6m },
                new EngineVersion { EngineName = "2.0 TDI 143 KM", PowerHP = 143, PowerKW = 105, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.2m, TopSpeedKmh = 205, FuelConsumptionCombined = 5.1m },
                new EngineVersion { EngineName = "2.0 TDI 177 KM", PowerHP = 177, PowerKW = 130, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.3m, TopSpeedKmh = 227, FuelConsumptionCombined = 5.3m },
                new EngineVersion { EngineName = "3.0 V6 TDI 245 KM", PowerHP = 245, PowerKW = 180, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 500, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 5.9m, TopSpeedKmh = 250, FuelConsumptionCombined = 6.5m },
            ]);

            int a5 = GetOrCreateModel(bId, "A5", "audi-a5");
            AddOrReplaceEngines(GetOrFixGeneration(a5, "I (2007–2016)", "audi-a5-i", 2007, 2016), 120, [
                new EngineVersion { EngineName = "1.8 TFSI 170 KM", PowerHP = 170, PowerKW = 125, Displacement = 1798, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.9m, TopSpeedKmh = 220, FuelConsumptionCombined = 7.1m },
                new EngineVersion { EngineName = "2.0 TFSI 211 KM", PowerHP = 211, PowerKW = 155, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 6.7m, TopSpeedKmh = 246, FuelConsumptionCombined = 7.8m },
                new EngineVersion { EngineName = "S5 3.0 TFSI 333 KM", PowerHP = 333, PowerKW = 245, Displacement = 2995, FuelTypeId = ben,
                    TorqueNm = 440, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 5.1m, TopSpeedKmh = 250, FuelConsumptionCombined = 9.6m },
                new EngineVersion { EngineName = "2.0 TDI 177 KM", PowerHP = 177, PowerKW = 130, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.3m, TopSpeedKmh = 227, FuelConsumptionCombined = 5.3m },
                new EngineVersion { EngineName = "3.0 V6 TDI 245 KM", PowerHP = 245, PowerKW = 180, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 500, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 5.9m, TopSpeedKmh = 250, FuelConsumptionCombined = 6.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(a5, "II (2016–)", "audi-a5-ii", 2016, null), 150, [
                new EngineVersion { EngineName = "35 TFSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1498, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.9m, TopSpeedKmh = 221, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "40 TFSI 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 1984, FuelTypeId = mild,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 6.9m, TopSpeedKmh = 240, FuelConsumptionCombined = 7.2m },
                new EngineVersion { EngineName = "S5 TFSI 354 KM", PowerHP = 354, PowerKW = 260, Displacement = 2995, FuelTypeId = mild,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 4.8m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.8m },
                new EngineVersion { EngineName = "35 TDI 163 KM", PowerHP = 163, PowerKW = 120, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.4m, TopSpeedKmh = 225, FuelConsumptionCombined = 4.6m },
                new EngineVersion { EngineName = "40 TDI 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 237, FuelConsumptionCombined = 5.0m },
            ]);

            int a6 = GetOrCreateModel(bId, "A6", "audi-a6");
            AddOrReplaceEngines(GetOrFixGeneration(a6, "C7 (2011–2018)", "audi-a6-c7", 2011, 2018), 150, [
                new EngineVersion { EngineName = "1.8 TFSI 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1798, FuelTypeId = ben,
                    TorqueNm = 320, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.2m, TopSpeedKmh = 228, FuelConsumptionCombined = 6.3m },
                new EngineVersion { EngineName = "2.0 TFSI 252 KM", PowerHP = 252, PowerKW = 185, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 370, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 6.1m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "S6 4.0 TFSI 450 KM", PowerHP = 450, PowerKW = 331, Displacement = 3993, FuelTypeId = ben,
                    TorqueNm = 550, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 8, Acceleration0100 = 4.4m, TopSpeedKmh = 250, FuelConsumptionCombined = 9.9m },
                new EngineVersion { EngineName = "2.0 TDI 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 231, FuelConsumptionCombined = 4.9m },
                new EngineVersion { EngineName = "3.0 V6 TDI 218 KM", PowerHP = 218, PowerKW = 160, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 450, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 6.5m, TopSpeedKmh = 246, FuelConsumptionCombined = 5.7m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(a6, "C8 (2018–)", "audi-a6-c8", 2018, null), 180, [
                new EngineVersion { EngineName = "40 TFSI 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 1984, FuelTypeId = mild,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 237, FuelConsumptionCombined = 6.6m },
                new EngineVersion { EngineName = "45 TFSI 265 KM", PowerHP = 265, PowerKW = 195, Displacement = 1984, FuelTypeId = mild,
                    TorqueNm = 370, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 5.9m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "S6 TDI 349 KM", PowerHP = 349, PowerKW = 257, Displacement = 2967, FuelTypeId = mild,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 5.0m, TopSpeedKmh = 250, FuelConsumptionCombined = 6.8m },
                new EngineVersion { EngineName = "40 TDI 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.9m, TopSpeedKmh = 237, FuelConsumptionCombined = 4.9m },
                new EngineVersion { EngineName = "50 TDI 286 KM", PowerHP = 286, PowerKW = 210, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 620, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 5.5m, TopSpeedKmh = 250, FuelConsumptionCombined = 6.0m },
            ]);

            int a7 = GetOrCreateModel(bId, "A7", "audi-a7");
            AddOrReplaceEngines(GetOrFixGeneration(a7, "I (2010–2018)", "audi-a7-i", 2010, 2018), 150, [
                new EngineVersion { EngineName = "2.0 TFSI 252 KM", PowerHP = 252, PowerKW = 185, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 370, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 6.4m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "S7 4.0 TFSI 450 KM", PowerHP = 450, PowerKW = 331, Displacement = 3993, FuelTypeId = ben,
                    TorqueNm = 550, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 8, Acceleration0100 = 4.7m, TopSpeedKmh = 250, FuelConsumptionCombined = 9.9m },
                new EngineVersion { EngineName = "3.0 V6 TDI 218 KM", PowerHP = 218, PowerKW = 160, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 450, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 6.5m, TopSpeedKmh = 246, FuelConsumptionCombined = 5.7m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(a7, "II (2018–)", "audi-a7-ii", 2018, null), 180, [
                new EngineVersion { EngineName = "45 TFSI 265 KM", PowerHP = 265, PowerKW = 195, Displacement = 1984, FuelTypeId = mild,
                    TorqueNm = 370, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 5.7m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "S7 TDI 349 KM", PowerHP = 349, PowerKW = 257, Displacement = 2967, FuelTypeId = mild,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 5.1m, TopSpeedKmh = 250, FuelConsumptionCombined = 6.8m },
                new EngineVersion { EngineName = "50 TDI 286 KM", PowerHP = 286, PowerKW = 210, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 620, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 5.6m, TopSpeedKmh = 250, FuelConsumptionCombined = 6.0m },
            ]);

            int a8 = GetOrCreateModel(bId, "A8", "audi-a8");
            AddOrReplaceEngines(GetOrFixGeneration(a8, "D4 (2010–2017)", "audi-a8-d4", 2010, 2017), 180, [
                new EngineVersion { EngineName = "3.0 TFSI 310 KM", PowerHP = 310, PowerKW = 228, Displacement = 2995, FuelTypeId = ben,
                    TorqueNm = 440, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 5.9m, TopSpeedKmh = 250, FuelConsumptionCombined = 9.5m },
                new EngineVersion { EngineName = "S8 4.0 TFSI 520 KM", PowerHP = 520, PowerKW = 382, Displacement = 3993, FuelTypeId = ben,
                    TorqueNm = 650, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "quattro",
                    Cylinders = 8, Acceleration0100 = 4.2m, TopSpeedKmh = 250, FuelConsumptionCombined = 10.7m },
                new EngineVersion { EngineName = "3.0 TDI 258 KM", PowerHP = 258, PowerKW = 190, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 580, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 5.8m, TopSpeedKmh = 250, FuelConsumptionCombined = 6.4m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(a8, "D5 (2017–)", "audi-a8-d5", 2017, null), 250, [
                new EngineVersion { EngineName = "55 TFSI 340 KM", PowerHP = 340, PowerKW = 250, Displacement = 2995, FuelTypeId = mild,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 5.7m, TopSpeedKmh = 250, FuelConsumptionCombined = 9.0m },
                new EngineVersion { EngineName = "60 TFSIe PHEV 449 KM", PowerHP = 449, PowerKW = 330, Displacement = 2995, FuelTypeId = phev,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 4.9m, TopSpeedKmh = 250, FuelConsumptionCombined = 2.4m },
                new EngineVersion { EngineName = "50 TDI 286 KM", PowerHP = 286, PowerKW = 210, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 600, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 6.1m, TopSpeedKmh = 250, FuelConsumptionCombined = 6.5m },
            ]);

            int q3 = GetOrCreateModel(bId, "Q3", "audi-q3");
            AddOrReplaceEngines(GetOrFixGeneration(q3, "I (2011–2018)", "audi-q3-i", 2011, 2018), 100, [
                new EngineVersion { EngineName = "1.4 TFSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1395, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.3m, TopSpeedKmh = 202, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "2.0 TFSI quattro 211 KM", PowerHP = 211, PowerKW = 155, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 6.6m, TopSpeedKmh = 231, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "2.0 TDI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 340, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.3m, TopSpeedKmh = 202, FuelConsumptionCombined = 5.3m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(q3, "II (2018–)", "audi-q3-ii", 2018, null), 120, [
                new EngineVersion { EngineName = "35 TFSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1498, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.2m, TopSpeedKmh = 205, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "45 TFSI quattro 230 KM", PowerHP = 230, PowerKW = 169, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 6.3m, TopSpeedKmh = 232, FuelConsumptionCombined = 7.9m },
                new EngineVersion { EngineName = "35 TDI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 340, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.3m, TopSpeedKmh = 206, FuelConsumptionCombined = 5.0m },
            ]);

            int q7 = GetOrCreateModel(bId, "Q7", "audi-q7");
            AddOrReplaceEngines(GetOrFixGeneration(q7, "I (2005–2015)", "audi-q7-i", 2005, 2015), 170, [
                new EngineVersion { EngineName = "3.6 FSI 280 KM", PowerHP = 280, PowerKW = 206, Displacement = 3597, FuelTypeId = ben,
                    TorqueNm = 360, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 8.4m, TopSpeedKmh = 216, FuelConsumptionCombined = 12.1m },
                new EngineVersion { EngineName = "3.0 TDI 245 KM", PowerHP = 245, PowerKW = 180, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 550, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 6.5m, TopSpeedKmh = 227, FuelConsumptionCombined = 7.2m },
                new EngineVersion { EngineName = "V12 TDI 500 KM", PowerHP = 500, PowerKW = 368, Displacement = 5934, FuelTypeId = die,
                    TorqueNm = 1000, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "quattro",
                    Cylinders = 12, Acceleration0100 = 5.5m, TopSpeedKmh = 250, FuelConsumptionCombined = 12.3m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(q7, "II (2015–)", "audi-q7-ii", 2015, null), 200, [
                new EngineVersion { EngineName = "45 TFSI 245 KM", PowerHP = 245, PowerKW = 180, Displacement = 1984, FuelTypeId = mild,
                    TorqueNm = 370, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 7.9m, TopSpeedKmh = 220, FuelConsumptionCombined = 8.6m },
                new EngineVersion { EngineName = "55 TFSI 340 KM", PowerHP = 340, PowerKW = 250, Displacement = 2995, FuelTypeId = mild,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 6.2m, TopSpeedKmh = 250, FuelConsumptionCombined = 9.7m },
                new EngineVersion { EngineName = "50 TDI 286 KM", PowerHP = 286, PowerKW = 210, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 600, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 6.3m, TopSpeedKmh = 245, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "60 TFSIe PHEV 462 KM", PowerHP = 462, PowerKW = 340, Displacement = 2995, FuelTypeId = phev,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 5.4m, TopSpeedKmh = 240, FuelConsumptionCombined = 2.7m },
            ]);

            int q8 = GetOrCreateModel(bId, "Q8", "audi-q8");
            AddOrReplaceEngines(GetOrFixGeneration(q8, "I (2018–)", "audi-q8-i", 2018, null), 250, [
                new EngineVersion { EngineName = "55 TFSI 340 KM", PowerHP = 340, PowerKW = 250, Displacement = 2995, FuelTypeId = mild,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 5.9m, TopSpeedKmh = 250, FuelConsumptionCombined = 9.7m },
                new EngineVersion { EngineName = "SQ8 TFSI 507 KM", PowerHP = 507, PowerKW = 373, Displacement = 3996, FuelTypeId = ben,
                    TorqueNm = 770, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 8, Acceleration0100 = 4.1m, TopSpeedKmh = 250, FuelConsumptionCombined = 12.9m },
                new EngineVersion { EngineName = "50 TDI 286 KM", PowerHP = 286, PowerKW = 210, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 600, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 6.3m, TopSpeedKmh = 245, FuelConsumptionCombined = 7.2m },
            ]);

            int tt = GetOrCreateModel(bId, "TT", "audi-tt");
            AddOrReplaceEngines(GetOrFixGeneration(tt, "8J (2006–2014)", "audi-tt-8j", 2006, 2014), 90, [
                new EngineVersion { EngineName = "1.8 TFSI 160 KM", PowerHP = 160, PowerKW = 118, Displacement = 1798, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.6m, TopSpeedKmh = 222, FuelConsumptionCombined = 6.9m },
                new EngineVersion { EngineName = "2.0 TFSI 211 KM", PowerHP = 211, PowerKW = 155, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 280, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 6.0m, TopSpeedKmh = 243, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "TTS 2.0 TFSI 272 KM", PowerHP = 272, PowerKW = 200, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 5.2m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "2.0 TDI 170 KM", PowerHP = 170, PowerKW = 125, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 350, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 226, FuelConsumptionCombined = 5.3m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(tt, "8S (2014–2023)", "audi-tt-8s", 2014, 2023), 180, [
                new EngineVersion { EngineName = "40 TFSI 197 KM", PowerHP = 197, PowerKW = 145, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.6m, TopSpeedKmh = 237, FuelConsumptionCombined = 6.4m },
                new EngineVersion { EngineName = "45 TFSI quattro 245 KM", PowerHP = 245, PowerKW = 180, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 370, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 5.2m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "TTS 2.0 TFSI 306 KM", PowerHP = 306, PowerKW = 225, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 4.7m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.8m },
                new EngineVersion { EngineName = "TT RS 2.5 TFSI 400 KM", PowerHP = 400, PowerKW = 294, Displacement = 2480, FuelTypeId = ben,
                    TorqueNm = 480, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 5, Acceleration0100 = 3.7m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.7m },
            ]);

            int a1 = GetOrCreateModel(bId, "A1", "audi-a1");
            PrepareGenerations(a1,
                ("I (2010–2018)", "audi-a1-i", 2010, 2018),
                ("II (2018–)", "audi-a1-ii", 2018, null));
            AddOrReplaceEngines(GetOrFixGeneration(a1, "I (2010–2018)", "audi-a1-i", 2010, 2018), 60, [
                new EngineVersion { EngineName = "1.0 TFSI 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 160, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.5m, TopSpeedKmh = 182, FuelConsumptionCombined = 5.0m },
                new EngineVersion { EngineName = "1.4 TFSI 122 KM", PowerHP = 122, PowerKW = 90, Displacement = 1395, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.9m, TopSpeedKmh = 203, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "S1 2.0 TFSI quattro 231 KM", PowerHP = 231, PowerKW = 170, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 370, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 5.8m, TopSpeedKmh = 246, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "1.6 TDI 116 KM", PowerHP = 116, PowerKW = 85, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 195, FuelConsumptionCombined = 3.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(a1, "II (2018–)", "audi-a1-ii", 2018, null), 60, [
                new EngineVersion { EngineName = "25 TFSI 95 KM", PowerHP = 95, PowerKW = 70, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 175, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 11.0m, TopSpeedKmh = 182, FuelConsumptionCombined = 5.0m },
                new EngineVersion { EngineName = "30 TFSI 116 KM", PowerHP = 116, PowerKW = 85, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.5m, TopSpeedKmh = 203, FuelConsumptionCombined = 5.1m },
                new EngineVersion { EngineName = "35 TFSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1498, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.4m, TopSpeedKmh = 220, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "S1 40 TFSI 231 KM", PowerHP = 231, PowerKW = 170, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.5m, TopSpeedKmh = 235, FuelConsumptionCombined = 6.8m },
            ]);

            int a2 = GetOrCreateModel(bId, "A2", "audi-a2");
            AddOrReplaceEngines(GetOrFixGeneration(a2, "I (1999–2005)", "audi-a2-i", 1999, 2005), 55, [
                new EngineVersion { EngineName = "1.4 75 KM", PowerHP = 75, PowerKW = 55, Displacement = 1390, FuelTypeId = ben,
                    TorqueNm = 126, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 13.9m, TopSpeedKmh = 168, FuelConsumptionCombined = 6.5m },
                new EngineVersion { EngineName = "1.6 FSI 110 KM", PowerHP = 110, PowerKW = 81, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 155, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 195, FuelConsumptionCombined = 6.4m },
                new EngineVersion { EngineName = "1.4 TDI 75 KM", PowerHP = 75, PowerKW = 55, Displacement = 1422, FuelTypeId = die,
                    TorqueNm = 195, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 14.9m, TopSpeedKmh = 165, FuelConsumptionCombined = 4.2m },
                new EngineVersion { EngineName = "1.4 TDI 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1422, FuelTypeId = die,
                    TorqueNm = 210, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 12.0m, TopSpeedKmh = 178, FuelConsumptionCombined = 4.4m },
            ]);

            int etron = GetOrCreateModel(bId, "e-tron", "audi-e-tron");
            AddOrReplaceEngines(GetOrFixGeneration(etron, "I (2018–2023)", "audi-e-tron-i", 2018, 2023), 300, [
                new EngineVersion { EngineName = "50 quattro 313 KM", PowerHP = 313, PowerKW = 230, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 540, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = null, Acceleration0100 = 6.8m, TopSpeedKmh = 190, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "55 quattro 408 KM", PowerHP = 408, PowerKW = 300, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 664, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = null, Acceleration0100 = 5.7m, TopSpeedKmh = 200, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "S quattro 503 KM", PowerHP = 503, PowerKW = 370, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 973, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = null, Acceleration0100 = 4.5m, TopSpeedKmh = 210, FuelConsumptionCombined = null },
            ]);

            int q2 = GetOrCreateModel(bId, "Q2", "audi-q2");
            AddOrReplaceEngines(GetOrFixGeneration(q2, "I (2016–)", "audi-q2-i", 2016, null), 100, [
                new EngineVersion { EngineName = "30 TFSI 116 KM", PowerHP = 116, PowerKW = 85, Displacement = 999, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.3m, TopSpeedKmh = 189, FuelConsumptionCombined = 5.7m },
                new EngineVersion { EngineName = "35 TFSI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1498, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.3m, TopSpeedKmh = 210, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "40 TFSI quattro 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 6.5m, TopSpeedKmh = 232, FuelConsumptionCombined = 7.2m },
                new EngineVersion { EngineName = "SQ2 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 4.9m, TopSpeedKmh = 235, FuelConsumptionCombined = 7.8m },
                new EngineVersion { EngineName = "30 TDI 116 KM", PowerHP = 116, PowerKW = 85, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.3m, TopSpeedKmh = 190, FuelConsumptionCombined = 4.3m },
                new EngineVersion { EngineName = "35 TDI 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 340, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 209, FuelConsumptionCombined = 4.6m },
            ]);

            int q5 = GetOrCreateModel(bId, "Q5", "audi-q5");
            PrepareGenerations(q5,
                ("I (2008–2017)", "audi-q5-i", 2008, 2017),
                ("II (2017–)", "audi-q5-ii", 2017, null));
            AddOrReplaceEngines(GetOrFixGeneration(q5, "I (2008–2017)", "audi-q5-i", 2008, 2017), 130, [
                new EngineVersion { EngineName = "2.0 TFSI 211 KM", PowerHP = 211, PowerKW = 155, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 7.2m, TopSpeedKmh = 217, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "SQ5 3.0 TFSI 354 KM", PowerHP = 354, PowerKW = 260, Displacement = 2995, FuelTypeId = ben,
                    TorqueNm = 500, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 5.1m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.7m },
                new EngineVersion { EngineName = "2.0 TDI 143 KM", PowerHP = 143, PowerKW = 105, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 9.6m, TopSpeedKmh = 197, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "2.0 TDI 177 KM", PowerHP = 177, PowerKW = 130, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 210, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "3.0 V6 TDI 245 KM", PowerHP = 245, PowerKW = 180, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 580, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 6.5m, TopSpeedKmh = 234, FuelConsumptionCombined = 6.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(q5, "II (2017–)", "audi-q5-ii", 2017, null), 130, [
                new EngineVersion { EngineName = "40 TFSI 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 7.9m, TopSpeedKmh = 213, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "45 TFSI 245 KM", PowerHP = 245, PowerKW = 180, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 370, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 6.3m, TopSpeedKmh = 237, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "SQ5 TFSI 354 KM", PowerHP = 354, PowerKW = 260, Displacement = 2995, FuelTypeId = ben,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 5.1m, TopSpeedKmh = 250, FuelConsumptionCombined = 9.0m },
                new EngineVersion { EngineName = "40 TDI 204 KM", PowerHP = 204, PowerKW = 150, Displacement = 1968, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 7.8m, TopSpeedKmh = 220, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "50 TDI 286 KM", PowerHP = 286, PowerKW = 210, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 620, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 5.8m, TopSpeedKmh = 245, FuelConsumptionCombined = 6.7m },
                new EngineVersion { EngineName = "SQ5 TDI 341 KM", PowerHP = 341, PowerKW = 251, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 5.1m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.2m },
            ]);

            int r8 = GetOrCreateModel(bId, "R8", "audi-r8");
            PrepareGenerations(r8,
                ("I (2007–2015)", "audi-r8-i", 2007, 2015),
                ("II (2015–2023)", "audi-r8-ii", 2015, 2023));
            AddOrReplaceEngines(GetOrFixGeneration(r8, "I (2007–2015)", "audi-r8-i", 2007, 2015), 400, [
                new EngineVersion { EngineName = "4.2 V8 420 KM", PowerHP = 420, PowerKW = 309, Displacement = 4163, FuelTypeId = ben,
                    TorqueNm = 430, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "quattro",
                    Cylinders = 8, Acceleration0100 = 4.6m, TopSpeedKmh = 301, FuelConsumptionCombined = 13.0m },
                new EngineVersion { EngineName = "5.2 V10 525 KM", PowerHP = 525, PowerKW = 386, Displacement = 5204, FuelTypeId = ben,
                    TorqueNm = 530, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "quattro",
                    Cylinders = 10, Acceleration0100 = 3.9m, TopSpeedKmh = 316, FuelConsumptionCombined = 14.9m },
                new EngineVersion { EngineName = "5.2 V10 Plus 550 KM", PowerHP = 550, PowerKW = 404, Displacement = 5204, FuelTypeId = ben,
                    TorqueNm = 540, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 10, Acceleration0100 = 3.5m, TopSpeedKmh = 317, FuelConsumptionCombined = 15.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(r8, "II (2015–2023)", "audi-r8-ii", 2015, 2023), 400, [
                new EngineVersion { EngineName = "5.2 V10 540 KM", PowerHP = 540, PowerKW = 397, Displacement = 5204, FuelTypeId = ben,
                    TorqueNm = 540, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 10, Acceleration0100 = 3.5m, TopSpeedKmh = 322, FuelConsumptionCombined = 12.3m },
                new EngineVersion { EngineName = "5.2 V10 Plus 610 KM", PowerHP = 610, PowerKW = 449, Displacement = 5204, FuelTypeId = ben,
                    TorqueNm = 560, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 10, Acceleration0100 = 3.2m, TopSpeedKmh = 330, FuelConsumptionCombined = 12.6m },
                new EngineVersion { EngineName = "5.2 V10 Performance 620 KM", PowerHP = 620, PowerKW = 456, Displacement = 5204, FuelTypeId = ben,
                    TorqueNm = 580, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 10, Acceleration0100 = 3.1m, TopSpeedKmh = 331, FuelConsumptionCombined = 12.9m },
            ]);

            int rs3 = GetOrCreateModel(bId, "RS3", "audi-rs3");
            PrepareGenerations(rs3,
                ("I (2011–2012)", "audi-rs3-i", 2011, 2012),
                ("II (2015–2020)", "audi-rs3-ii", 2015, 2020),
                ("III (2021–)", "audi-rs3-iii", 2021, null));
            AddOrReplaceEngines(GetOrFixGeneration(rs3, "I (2011–2012)", "audi-rs3-i", 2011, 2012), 300, [
                new EngineVersion { EngineName = "2.5 TFSI 340 KM", PowerHP = 340, PowerKW = 250, Displacement = 2480, FuelTypeId = ben,
                    TorqueNm = 450, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 5, Acceleration0100 = 4.6m, TopSpeedKmh = 250, FuelConsumptionCombined = 9.1m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(rs3, "II (2015–2020)", "audi-rs3-ii", 2015, 2020), 300, [
                new EngineVersion { EngineName = "2.5 TFSI 367 KM", PowerHP = 367, PowerKW = 270, Displacement = 2480, FuelTypeId = ben,
                    TorqueNm = 465, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 5, Acceleration0100 = 4.3m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.4m },
                new EngineVersion { EngineName = "2.5 TFSI 400 KM", PowerHP = 400, PowerKW = 294, Displacement = 2480, FuelTypeId = ben,
                    TorqueNm = 480, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 5, Acceleration0100 = 4.1m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(rs3, "III (2021–)", "audi-rs3-iii", 2021, null), 300, [
                new EngineVersion { EngineName = "2.5 TFSI 400 KM", PowerHP = 400, PowerKW = 294, Displacement = 2480, FuelTypeId = ben,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 5, Acceleration0100 = 3.8m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.8m },
            ]);

            int rs5 = GetOrCreateModel(bId, "RS5", "audi-rs5");
            PrepareGenerations(rs5,
                ("I (2010–2016)", "audi-rs5-i", 2010, 2016),
                ("II (2017–)", "audi-rs5-ii", 2017, null));
            AddOrReplaceEngines(GetOrFixGeneration(rs5, "I (2010–2016)", "audi-rs5-i", 2010, 2016), 400, [
                new EngineVersion { EngineName = "4.2 V8 450 KM", PowerHP = 450, PowerKW = 331, Displacement = 4163, FuelTypeId = ben,
                    TorqueNm = 430, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 8, Acceleration0100 = 4.6m, TopSpeedKmh = 250, FuelConsumptionCombined = 10.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(rs5, "II (2017–)", "audi-rs5-ii", 2017, null), 400, [
                new EngineVersion { EngineName = "2.9 TFSI 450 KM", PowerHP = 450, PowerKW = 331, Displacement = 2894, FuelTypeId = ben,
                    TorqueNm = 600, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 3.9m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.7m },
            ]);

            int s3 = GetOrCreateModel(bId, "S3", "audi-s3");
            PrepareGenerations(s3,
                ("I (1999–2003)", "audi-s3-i", 1999, 2003),
                ("II (2006–2012)", "audi-s3-ii", 2006, 2012),
                ("III (2013–2020)", "audi-s3-iii", 2013, 2020),
                ("IV (2021–)", "audi-s3-iv", 2021, null));
            AddOrReplaceEngines(GetOrFixGeneration(s3, "I (1999–2003)", "audi-s3-i", 1999, 2003), 180, [
                new EngineVersion { EngineName = "1.8 T quattro 210 KM", PowerHP = 210, PowerKW = 154, Displacement = 1781, FuelTypeId = ben,
                    TorqueNm = 280, EuroNorm = "Euro 3", GearboxType = "manual", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 6.7m, TopSpeedKmh = 235, FuelConsumptionCombined = 9.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(s3, "II (2006–2012)", "audi-s3-ii", 2006, 2012), 240, [
                new EngineVersion { EngineName = "2.0 TFSI quattro 265 KM", PowerHP = 265, PowerKW = 195, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 5", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 5.2m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(s3, "III (2013–2020)", "audi-s3-iii", 2013, 2020), 280, [
                new EngineVersion { EngineName = "2.0 TFSI quattro 300 KM", PowerHP = 300, PowerKW = 221, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 380, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 4.9m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.1m },
                new EngineVersion { EngineName = "2.0 TFSI quattro 310 KM", PowerHP = 310, PowerKW = 228, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 4.7m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.3m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(s3, "IV (2021–)", "audi-s3-iv", 2021, null), 280, [
                new EngineVersion { EngineName = "2.0 TFSI quattro 310 KM", PowerHP = 310, PowerKW = 228, Displacement = 1984, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 4, Acceleration0100 = 4.8m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.4m },
            ]);

            int sq5 = GetOrCreateModel(bId, "SQ5", "audi-sq5");
            PrepareGenerations(sq5,
                ("I (2013–2017)", "audi-sq5-i", 2013, 2017),
                ("II (2017–)", "audi-sq5-ii", 2017, null));
            AddOrReplaceEngines(GetOrFixGeneration(sq5, "I (2013–2017)", "audi-sq5-i", 2013, 2017), 250, [
                new EngineVersion { EngineName = "3.0 TDI biturbo 313 KM", PowerHP = 313, PowerKW = 230, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 650, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 5.1m, TopSpeedKmh = 250, FuelConsumptionCombined = 6.9m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(sq5, "II (2017–)", "audi-sq5-ii", 2017, null), 250, [
                new EngineVersion { EngineName = "3.0 TFSI 354 KM", PowerHP = 354, PowerKW = 260, Displacement = 2995, FuelTypeId = ben,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 5.1m, TopSpeedKmh = 250, FuelConsumptionCombined = 9.0m },
                new EngineVersion { EngineName = "3.0 TDI 341 KM", PowerHP = 341, PowerKW = 251, Displacement = 2967, FuelTypeId = die,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "quattro",
                    Cylinders = 6, Acceleration0100 = 5.1m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.2m },
            ]);
        }

        // ── BMW SERIA 1/2/4/6/7/8 + X1/X2/X3/X4/X6/X7 ────────────────────────────
        {
            int bId = GetOrCreateBrand("BMW", "bmw", "auta-osobowe");

            int s1 = GetOrCreateModel(bId, "Seria 1", "bmw-seria-1");
            AddOrReplaceEngines(GetOrFixGeneration(s1, "E87 (2004–2011)", "bmw-1-e87", 2004, 2011), 90, [
                new EngineVersion { EngineName = "116i 122 KM", PowerHP = 122, PowerKW = 90, Displacement = 1596, FuelTypeId = ben,
                    TorqueNm = 150, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 10.4m, TopSpeedKmh = 195, FuelConsumptionCombined = 6.9m },
                new EngineVersion { EngineName = "120i 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1995, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 8.7m, TopSpeedKmh = 210, FuelConsumptionCombined = 7.3m },
                new EngineVersion { EngineName = "130i 265 KM", PowerHP = 265, PowerKW = 195, Displacement = 2996, FuelTypeId = ben,
                    TorqueNm = 315, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 6.1m, TopSpeedKmh = 250, FuelConsumptionCombined = 9.9m },
                new EngineVersion { EngineName = "118d 143 KM", PowerHP = 143, PowerKW = 105, Displacement = 1995, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 9.4m, TopSpeedKmh = 208, FuelConsumptionCombined = 5.0m },
                new EngineVersion { EngineName = "120d 177 KM", PowerHP = 177, PowerKW = 130, Displacement = 1995, FuelTypeId = die,
                    TorqueNm = 350, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 222, FuelConsumptionCombined = 5.3m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(s1, "F20 (2011–2019)", "bmw-1-f20", 2011, 2019), 100, [
                new EngineVersion { EngineName = "116i 109 KM", PowerHP = 109, PowerKW = 80, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 180, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, Acceleration0100 = 10.9m, TopSpeedKmh = 195, FuelConsumptionCombined = 5.7m },
                new EngineVersion { EngineName = "120i 178 KM", PowerHP = 178, PowerKW = 131, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 280, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.4m, TopSpeedKmh = 235, FuelConsumptionCombined = 6.6m },
                new EngineVersion { EngineName = "M140i 340 KM", PowerHP = 340, PowerKW = 250, Displacement = 2998, FuelTypeId = ben,
                    TorqueNm = 500, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 4.6m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "116d 116 KM", PowerHP = 116, PowerKW = 85, Displacement = 1496, FuelTypeId = die,
                    TorqueNm = 270, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, Acceleration0100 = 10.9m, TopSpeedKmh = 195, FuelConsumptionCombined = 4.0m },
                new EngineVersion { EngineName = "120d 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1995, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.3m, TopSpeedKmh = 235, FuelConsumptionCombined = 4.9m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(s1, "F40 (2019–)", "bmw-1-f40", 2019, null), 110, [
                new EngineVersion { EngineName = "116i 109 KM", PowerHP = 109, PowerKW = 80, Displacement = 1499, FuelTypeId = mild,
                    TorqueNm = 190, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.7m, TopSpeedKmh = 195, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "118i 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1499, FuelTypeId = mild,
                    TorqueNm = 230, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 8.5m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "M135i xDrive 306 KM", PowerHP = 306, PowerKW = 225, Displacement = 1998, FuelTypeId = mild,
                    TorqueNm = 450, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 4.8m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.1m },
                new EngineVersion { EngineName = "116d 116 KM", PowerHP = 116, PowerKW = 85, Displacement = 1496, FuelTypeId = die,
                    TorqueNm = 270, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.5m, TopSpeedKmh = 200, FuelConsumptionCombined = 4.3m },
                new EngineVersion { EngineName = "120d xDrive 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1995, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.3m, TopSpeedKmh = 233, FuelConsumptionCombined = 5.0m },
            ]);

            int s2 = GetOrCreateModel(bId, "Seria 2", "bmw-seria-2");
            AddOrReplaceEngines(GetOrFixGeneration(s2, "F22 (2014–2021)", "bmw-2-f22", 2014, 2021), 110, [
                new EngineVersion { EngineName = "218i 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1499, FuelTypeId = ben,
                    TorqueNm = 220, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 3, Acceleration0100 = 8.9m, TopSpeedKmh = 210, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "220i 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 290, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.1m, TopSpeedKmh = 235, FuelConsumptionCombined = 6.6m },
                new EngineVersion { EngineName = "M240i 340 KM", PowerHP = 340, PowerKW = 250, Displacement = 2998, FuelTypeId = ben,
                    TorqueNm = 500, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 4.6m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "218d 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1995, FuelTypeId = die,
                    TorqueNm = 330, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 8.9m, TopSpeedKmh = 213, FuelConsumptionCombined = 4.5m },
                new EngineVersion { EngineName = "220d 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1995, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.1m, TopSpeedKmh = 235, FuelConsumptionCombined = 4.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(s2, "G42 (2021–)", "bmw-2-g42", 2021, null), 130, [
                new EngineVersion { EngineName = "220i 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 1998, FuelTypeId = mild,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.0m, TopSpeedKmh = 240, FuelConsumptionCombined = 6.6m },
                new EngineVersion { EngineName = "M240i xDrive 374 KM", PowerHP = 374, PowerKW = 275, Displacement = 2998, FuelTypeId = mild,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 4.3m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.4m },
                new EngineVersion { EngineName = "220d 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1995, FuelTypeId = mild,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.0m, TopSpeedKmh = 238, FuelConsumptionCombined = 4.9m },
            ]);

            int s4 = GetOrCreateModel(bId, "Seria 4", "bmw-seria-4");
            AddOrReplaceEngines(GetOrFixGeneration(s4, "F32 (2013–2020)", "bmw-4-f32", 2013, 2020), 130, [
                new EngineVersion { EngineName = "420i 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 290, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 235, FuelConsumptionCombined = 6.6m },
                new EngineVersion { EngineName = "430i 252 KM", PowerHP = 252, PowerKW = 185, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 5.8m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "M4 431 KM", PowerHP = 431, PowerKW = 317, Displacement = 2979, FuelTypeId = ben,
                    TorqueNm = 550, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 4.1m, TopSpeedKmh = 250, FuelConsumptionCombined = 9.9m },
                new EngineVersion { EngineName = "420d 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1995, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.3m, TopSpeedKmh = 233, FuelConsumptionCombined = 4.8m },
                new EngineVersion { EngineName = "430d 258 KM", PowerHP = 258, PowerKW = 190, Displacement = 2993, FuelTypeId = die,
                    TorqueNm = 560, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 5.5m, TopSpeedKmh = 250, FuelConsumptionCombined = 5.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(s4, "G22 (2020–)", "bmw-4-g22", 2020, null), 150, [
                new EngineVersion { EngineName = "420i 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 1998, FuelTypeId = mild,
                    TorqueNm = 300, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.3m, TopSpeedKmh = 240, FuelConsumptionCombined = 6.8m },
                new EngineVersion { EngineName = "430i 258 KM", PowerHP = 258, PowerKW = 190, Displacement = 1998, FuelTypeId = mild,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 5.8m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.2m },
                new EngineVersion { EngineName = "M4 Competition 510 KM", PowerHP = 510, PowerKW = 375, Displacement = 2993, FuelTypeId = ben,
                    TorqueNm = 650, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 3.9m, TopSpeedKmh = 290, FuelConsumptionCombined = 10.6m },
                new EngineVersion { EngineName = "420d 197 KM", PowerHP = 197, PowerKW = 145, Displacement = 1995, FuelTypeId = mild,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 7.0m, TopSpeedKmh = 240, FuelConsumptionCombined = 4.9m },
                new EngineVersion { EngineName = "430d 286 KM", PowerHP = 286, PowerKW = 210, Displacement = 2993, FuelTypeId = mild,
                    TorqueNm = 580, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.2m, TopSpeedKmh = 250, FuelConsumptionCombined = 5.6m },
            ]);

            int s6 = GetOrCreateModel(bId, "Seria 6", "bmw-seria-6");
            AddOrReplaceEngines(GetOrFixGeneration(s6, "F12/F13 (2011–2018)", "bmw-6-f12", 2011, 2018), 250, [
                new EngineVersion { EngineName = "640i 320 KM", PowerHP = 320, PowerKW = 235, Displacement = 2979, FuelTypeId = ben,
                    TorqueNm = 450, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 5.2m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.7m },
                new EngineVersion { EngineName = "M6 560 KM", PowerHP = 560, PowerKW = 412, Displacement = 4395, FuelTypeId = ben,
                    TorqueNm = 680, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 4.2m, TopSpeedKmh = 250, FuelConsumptionCombined = 10.4m },
                new EngineVersion { EngineName = "630d 258 KM", PowerHP = 258, PowerKW = 190, Displacement = 2993, FuelTypeId = die,
                    TorqueNm = 560, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 5.8m, TopSpeedKmh = 250, FuelConsumptionCombined = 5.7m },
            ]);

            int s7 = GetOrCreateModel(bId, "Seria 7", "bmw-seria-7");
            AddOrReplaceEngines(GetOrFixGeneration(s7, "F01 (2008–2015)", "bmw-7-f01", 2008, 2015), 180, [
                new EngineVersion { EngineName = "730i 258 KM", PowerHP = 258, PowerKW = 190, Displacement = 2996, FuelTypeId = ben,
                    TorqueNm = 310, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 7.6m, TopSpeedKmh = 240, FuelConsumptionCombined = 9.5m },
                new EngineVersion { EngineName = "750i 407 KM", PowerHP = 407, PowerKW = 300, Displacement = 4395, FuelTypeId = ben,
                    TorqueNm = 600, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 5.0m, TopSpeedKmh = 250, FuelConsumptionCombined = 10.7m },
                new EngineVersion { EngineName = "730d 245 KM", PowerHP = 245, PowerKW = 180, Displacement = 2993, FuelTypeId = die,
                    TorqueNm = 540, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 6.7m, TopSpeedKmh = 245, FuelConsumptionCombined = 6.3m },
                new EngineVersion { EngineName = "750d 381 KM", PowerHP = 381, PowerKW = 280, Displacement = 2993, FuelTypeId = die,
                    TorqueNm = 740, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 4.9m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.2m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(s7, "G11 (2015–2022)", "bmw-7-g11", 2015, 2022), 250, [
                new EngineVersion { EngineName = "740i 326 KM", PowerHP = 326, PowerKW = 240, Displacement = 2998, FuelTypeId = mild,
                    TorqueNm = 450, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 5.6m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "750i xDrive 530 KM", PowerHP = 530, PowerKW = 390, Displacement = 4395, FuelTypeId = mild,
                    TorqueNm = 750, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.0m, TopSpeedKmh = 250, FuelConsumptionCombined = 11.0m },
                new EngineVersion { EngineName = "730d 265 KM", PowerHP = 265, PowerKW = 195, Displacement = 2993, FuelTypeId = mild,
                    TorqueNm = 620, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 6.1m, TopSpeedKmh = 250, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "745e PHEV 394 KM", PowerHP = 394, PowerKW = 290, Displacement = 2998, FuelTypeId = phev,
                    TorqueNm = 600, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 5.2m, TopSpeedKmh = 250, FuelConsumptionCombined = 2.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(s7, "G70 (2022–)", "bmw-7-g70", 2022, null), 300, [
                new EngineVersion { EngineName = "740i 381 KM", PowerHP = 381, PowerKW = 280, Displacement = 2998, FuelTypeId = mild,
                    TorqueNm = 540, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.2m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.1m },
                new EngineVersion { EngineName = "760i xDrive 544 KM", PowerHP = 544, PowerKW = 400, Displacement = 4395, FuelTypeId = mild,
                    TorqueNm = 750, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.2m, TopSpeedKmh = 250, FuelConsumptionCombined = 11.5m },
                new EngineVersion { EngineName = "i7 xDrive60 544 KM (EV)", PowerHP = 544, PowerKW = 400, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 745, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 4.7m, TopSpeedKmh = 240, FuelConsumptionCombined = null },
            ]);

            int s8 = GetOrCreateModel(bId, "Seria 8", "bmw-seria-8");
            AddOrReplaceEngines(GetOrFixGeneration(s8, "G14/G15 (2018–)", "bmw-8-g14", 2018, null), 300, [
                new EngineVersion { EngineName = "840i 340 KM", PowerHP = 340, PowerKW = 250, Displacement = 2998, FuelTypeId = mild,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 5.0m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.4m },
                new EngineVersion { EngineName = "M850i xDrive 530 KM", PowerHP = 530, PowerKW = 390, Displacement = 4395, FuelTypeId = mild,
                    TorqueNm = 750, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 3.7m, TopSpeedKmh = 250, FuelConsumptionCombined = 11.1m },
                new EngineVersion { EngineName = "M8 Competition 625 KM", PowerHP = 625, PowerKW = 460, Displacement = 4395, FuelTypeId = ben,
                    TorqueNm = 750, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 3.2m, TopSpeedKmh = 305, FuelConsumptionCombined = 11.7m },
                new EngineVersion { EngineName = "840d xDrive 320 KM", PowerHP = 320, PowerKW = 235, Displacement = 2993, FuelTypeId = die,
                    TorqueNm = 680, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 4.9m, TopSpeedKmh = 250, FuelConsumptionCombined = 6.6m },
            ]);

            int x1 = GetOrCreateModel(bId, "X1", "bmw-x1");
            AddOrReplaceEngines(GetOrFixGeneration(x1, "E84 (2009–2015)", "bmw-x1-e84", 2009, 2015), 90, [
                new EngineVersion { EngineName = "sDrive18i 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1995, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 9.3m, TopSpeedKmh = 200, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "sDrive18d 143 KM", PowerHP = 143, PowerKW = 105, Displacement = 1995, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 195, FuelConsumptionCombined = 5.2m },
                new EngineVersion { EngineName = "xDrive20d 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 1995, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.1m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(x1, "F48 (2015–2022)", "bmw-x1-f48", 2015, 2022), 100, [
                new EngineVersion { EngineName = "sDrive18i 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1499, FuelTypeId = ben,
                    TorqueNm = 220, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 10.0m, TopSpeedKmh = 195, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "xDrive25i 231 KM", PowerHP = 231, PowerKW = 170, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.5m, TopSpeedKmh = 235, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "sDrive18d 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1995, FuelTypeId = die,
                    TorqueNm = 330, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.3m, TopSpeedKmh = 205, FuelConsumptionCombined = 4.6m },
                new EngineVersion { EngineName = "xDrive20d 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1995, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.6m, TopSpeedKmh = 213, FuelConsumptionCombined = 5.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(x1, "U11 (2022–)", "bmw-x1-u11", 2022, null), 120, [
                new EngineVersion { EngineName = "sDrive18i 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1499, FuelTypeId = mild,
                    TorqueNm = 230, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.9m, TopSpeedKmh = 199, FuelConsumptionCombined = 6.3m },
                new EngineVersion { EngineName = "xDrive23i 218 KM", PowerHP = 218, PowerKW = 160, Displacement = 1998, FuelTypeId = mild,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.0m, TopSpeedKmh = 227, FuelConsumptionCombined = 7.2m },
                new EngineVersion { EngineName = "sDrive18d 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1995, FuelTypeId = die,
                    TorqueNm = 360, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.3m, TopSpeedKmh = 208, FuelConsumptionCombined = 5.0m },
                new EngineVersion { EngineName = "xDrive23d 211 KM", PowerHP = 211, PowerKW = 155, Displacement = 1995, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.9m, TopSpeedKmh = 226, FuelConsumptionCombined = 5.6m },
            ]);

            int x2 = GetOrCreateModel(bId, "X2", "bmw-x2");
            AddOrReplaceEngines(GetOrFixGeneration(x2, "F39 (2018–)", "bmw-x2-f39", 2018, null), 100, [
                new EngineVersion { EngineName = "sDrive18i 140 KM", PowerHP = 140, PowerKW = 103, Displacement = 1499, FuelTypeId = ben,
                    TorqueNm = 220, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 3, Acceleration0100 = 9.9m, TopSpeedKmh = 195, FuelConsumptionCombined = 6.3m },
                new EngineVersion { EngineName = "xDrive25i 231 KM", PowerHP = 231, PowerKW = 170, Displacement = 1998, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.3m, TopSpeedKmh = 235, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "sDrive18d 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1995, FuelTypeId = die,
                    TorqueNm = 330, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.1m, TopSpeedKmh = 206, FuelConsumptionCombined = 4.7m },
            ]);

            int x3 = GetOrCreateModel(bId, "X3", "bmw-x3");
            AddOrReplaceEngines(GetOrFixGeneration(x3, "F25 (2010–2017)", "bmw-x3-f25", 2010, 2017), 130, [
                new EngineVersion { EngineName = "xDrive20i 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 1997, FuelTypeId = ben,
                    TorqueNm = 270, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 210, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "xDrive35i 306 KM", PowerHP = 306, PowerKW = 225, Displacement = 2979, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.7m, TopSpeedKmh = 245, FuelConsumptionCombined = 9.5m },
                new EngineVersion { EngineName = "xDrive20d 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1995, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.1m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.7m },
                new EngineVersion { EngineName = "xDrive30d 258 KM", PowerHP = 258, PowerKW = 190, Displacement = 2993, FuelTypeId = die,
                    TorqueNm = 560, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.0m, TopSpeedKmh = 230, FuelConsumptionCombined = 6.3m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(x3, "G01 (2017–)", "bmw-x3-g01", 2017, null), 150, [
                new EngineVersion { EngineName = "xDrive20i 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 1998, FuelTypeId = mild,
                    TorqueNm = 290, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.1m, TopSpeedKmh = 210, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "xDrive30i 245 KM", PowerHP = 245, PowerKW = 180, Displacement = 1998, FuelTypeId = mild,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.4m, TopSpeedKmh = 230, FuelConsumptionCombined = 8.1m },
                new EngineVersion { EngineName = "X3 M Competition 510 KM", PowerHP = 510, PowerKW = 375, Displacement = 2993, FuelTypeId = ben,
                    TorqueNm = 600, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 3.8m, TopSpeedKmh = 285, FuelConsumptionCombined = 10.7m },
                new EngineVersion { EngineName = "xDrive20d 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1995, FuelTypeId = mild,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 213, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "xDrive30d 286 KM", PowerHP = 286, PowerKW = 210, Displacement = 2993, FuelTypeId = mild,
                    TorqueNm = 620, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.8m, TopSpeedKmh = 240, FuelConsumptionCombined = 6.1m },
            ]);

            int x4 = GetOrCreateModel(bId, "X4", "bmw-x4");
            AddOrReplaceEngines(GetOrFixGeneration(x4, "F26 (2014–2018)", "bmw-x4-f26", 2014, 2018), 130, [
                new EngineVersion { EngineName = "xDrive20i 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 1997, FuelTypeId = ben,
                    TorqueNm = 270, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.3m, TopSpeedKmh = 210, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "xDrive20d 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1995, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.7m },
                new EngineVersion { EngineName = "xDrive30d 258 KM", PowerHP = 258, PowerKW = 190, Displacement = 2993, FuelTypeId = die,
                    TorqueNm = 560, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.8m, TopSpeedKmh = 230, FuelConsumptionCombined = 6.3m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(x4, "G02 (2018–)", "bmw-x4-g02", 2018, null), 150, [
                new EngineVersion { EngineName = "xDrive20i 184 KM", PowerHP = 184, PowerKW = 135, Displacement = 1998, FuelTypeId = mild,
                    TorqueNm = 290, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 210, FuelConsumptionCombined = 7.6m },
                new EngineVersion { EngineName = "X4 M Competition 510 KM", PowerHP = 510, PowerKW = 375, Displacement = 2993, FuelTypeId = ben,
                    TorqueNm = 600, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 3.8m, TopSpeedKmh = 285, FuelConsumptionCombined = 10.7m },
                new EngineVersion { EngineName = "xDrive20d 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1995, FuelTypeId = mild,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.9m, TopSpeedKmh = 213, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "xDrive30d 286 KM", PowerHP = 286, PowerKW = 210, Displacement = 2993, FuelTypeId = mild,
                    TorqueNm = 620, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.8m, TopSpeedKmh = 240, FuelConsumptionCombined = 6.1m },
            ]);

            int x6 = GetOrCreateModel(bId, "X6", "bmw-x6");
            AddOrReplaceEngines(GetOrFixGeneration(x6, "E71 (2008–2014)", "bmw-x6-e71", 2008, 2014), 200, [
                new EngineVersion { EngineName = "xDrive35i 306 KM", PowerHP = 306, PowerKW = 225, Displacement = 2979, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.8m, TopSpeedKmh = 230, FuelConsumptionCombined = 10.8m },
                new EngineVersion { EngineName = "xDrive30d 245 KM", PowerHP = 245, PowerKW = 180, Displacement = 2993, FuelTypeId = die,
                    TorqueNm = 540, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 7.6m, TopSpeedKmh = 222, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "xDrive50i 407 KM", PowerHP = 407, PowerKW = 300, Displacement = 4395, FuelTypeId = ben,
                    TorqueNm = 600, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 5.4m, TopSpeedKmh = 250, FuelConsumptionCombined = 11.3m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(x6, "F16 (2014–2019)", "bmw-x6-f16", 2014, 2019), 250, [
                new EngineVersion { EngineName = "xDrive35i 306 KM", PowerHP = 306, PowerKW = 225, Displacement = 2979, FuelTypeId = ben,
                    TorqueNm = 400, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.5m, TopSpeedKmh = 230, FuelConsumptionCombined = 9.9m },
                new EngineVersion { EngineName = "xDrive30d 258 KM", PowerHP = 258, PowerKW = 190, Displacement = 2993, FuelTypeId = die,
                    TorqueNm = 560, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.7m, TopSpeedKmh = 230, FuelConsumptionCombined = 6.3m },
                new EngineVersion { EngineName = "M50d 381 KM", PowerHP = 381, PowerKW = 280, Displacement = 2993, FuelTypeId = die,
                    TorqueNm = 740, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.2m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.2m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(x6, "G06 (2019–)", "bmw-x6-g06", 2019, null), 280, [
                new EngineVersion { EngineName = "xDrive30d 286 KM", PowerHP = 286, PowerKW = 210, Displacement = 2993, FuelTypeId = mild,
                    TorqueNm = 620, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.1m, TopSpeedKmh = 240, FuelConsumptionCombined = 6.6m },
                new EngineVersion { EngineName = "xDrive40i 340 KM", PowerHP = 340, PowerKW = 250, Displacement = 2998, FuelTypeId = mild,
                    TorqueNm = 450, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.4m, TopSpeedKmh = 240, FuelConsumptionCombined = 8.9m },
                new EngineVersion { EngineName = "X6 M Competition 625 KM", PowerHP = 625, PowerKW = 460, Displacement = 4395, FuelTypeId = ben,
                    TorqueNm = 750, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 3.8m, TopSpeedKmh = 290, FuelConsumptionCombined = 12.1m },
            ]);

            int x7 = GetOrCreateModel(bId, "X7", "bmw-x7");
            AddOrReplaceEngines(GetOrFixGeneration(x7, "G07 (2019–)", "bmw-x7-g07", 2019, null), 280, [
                new EngineVersion { EngineName = "xDrive40i 340 KM", PowerHP = 340, PowerKW = 250, Displacement = 2998, FuelTypeId = mild,
                    TorqueNm = 450, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.1m, TopSpeedKmh = 243, FuelConsumptionCombined = 9.3m },
                new EngineVersion { EngineName = "M60i 530 KM", PowerHP = 530, PowerKW = 390, Displacement = 4395, FuelTypeId = mild,
                    TorqueNm = 750, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.7m, TopSpeedKmh = 250, FuelConsumptionCombined = 12.0m },
                new EngineVersion { EngineName = "xDrive30d 286 KM", PowerHP = 286, PowerKW = 210, Displacement = 2993, FuelTypeId = mild,
                    TorqueNm = 620, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.4m, TopSpeedKmh = 234, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "xDrive40d 352 KM", PowerHP = 352, PowerKW = 259, Displacement = 2993, FuelTypeId = mild,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.4m, TopSpeedKmh = 240, FuelConsumptionCombined = 7.5m },
            ]);

            int i3 = GetOrCreateModel(bId, "i3", "bmw-i3");
            AddOrReplaceEngines(GetOrFixGeneration(i3, "I (2013–2022)", "bmw-i3-i", 2013, 2022), 130, [
                new EngineVersion { EngineName = "170 KM", PowerHP = 170, PowerKW = 125, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 250, EuroNorm = "Euro 6", GearboxType = "eAutomatic", DriveType = "RWD",
                    Cylinders = null, Acceleration0100 = 7.3m, TopSpeedKmh = 150, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "i3s 184 KM", PowerHP = 184, PowerKW = 135, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 270, EuroNorm = "Euro 6", GearboxType = "eAutomatic", DriveType = "RWD",
                    Cylinders = null, Acceleration0100 = 6.9m, TopSpeedKmh = 160, FuelConsumptionCombined = null },
            ]);

            int i4 = GetOrCreateModel(bId, "i4", "bmw-i4");
            AddOrReplaceEngines(GetOrFixGeneration(i4, "I (2021–)", "bmw-i4-i", 2021, null), 300, [
                new EngineVersion { EngineName = "eDrive40 340 KM", PowerHP = 340, PowerKW = 250, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 430, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "RWD",
                    Cylinders = null, Acceleration0100 = 5.7m, TopSpeedKmh = 190, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "M50 544 KM", PowerHP = 544, PowerKW = 400, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 795, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = null, Acceleration0100 = 3.9m, TopSpeedKmh = 225, FuelConsumptionCombined = null },
            ]);

            int ix = GetOrCreateModel(bId, "iX", "bmw-ix");
            AddOrReplaceEngines(GetOrFixGeneration(ix, "I (2021–)", "bmw-ix-i", 2021, null), 300, [
                new EngineVersion { EngineName = "xDrive40 326 KM", PowerHP = 326, PowerKW = 240, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 630, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = null, Acceleration0100 = 6.1m, TopSpeedKmh = 200, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "xDrive50 523 KM", PowerHP = 523, PowerKW = 385, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 765, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = null, Acceleration0100 = 4.6m, TopSpeedKmh = 200, FuelConsumptionCombined = null },
                new EngineVersion { EngineName = "M60 619 KM", PowerHP = 619, PowerKW = 455, Displacement = null, FuelTypeId = ev,
                    TorqueNm = 1015, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = null, Acceleration0100 = 3.8m, TopSpeedKmh = 250, FuelConsumptionCombined = null },
            ]);
        }

        // ── MERCEDES-BENZ A/B/CLA/CLS/GLA/GLB/GLE/GLS/G-KLASA/AMG GT ─────────────
        {
            int bId = GetOrCreateBrand("Mercedes-Benz", "mercedes-benz", "auta-osobowe");

            int klasaA = GetOrCreateModel(bId, "Klasa A", "mb-klasa-a");
            AddOrReplaceEngines(GetOrFixGeneration(klasaA, "W176 (2012–2018)", "mb-a-w176", 2012, 2018), 90, [
                new EngineVersion { EngineName = "A160 102 KM", PowerHP = 102, PowerKW = 75, Displacement = 1595, FuelTypeId = ben,
                    TorqueNm = 155, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.7m, TopSpeedKmh = 190, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "A200 156 KM", PowerHP = 156, PowerKW = 115, Displacement = 1595, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.4m, TopSpeedKmh = 218, FuelConsumptionCombined = 5.8m },
                new EngineVersion { EngineName = "A45 AMG 381 KM", PowerHP = 381, PowerKW = 280, Displacement = 1991, FuelTypeId = ben,
                    TorqueNm = 475, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 4.2m, TopSpeedKmh = 270, FuelConsumptionCombined = 7.3m },
                new EngineVersion { EngineName = "A180d 109 KM", PowerHP = 109, PowerKW = 80, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 260, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.0m, TopSpeedKmh = 190, FuelConsumptionCombined = 3.8m },
                new EngineVersion { EngineName = "A200d 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 2143, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.0m, TopSpeedKmh = 209, FuelConsumptionCombined = 4.1m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(klasaA, "W177 (2018–)", "mb-a-w177", 2018, null), 100, [
                new EngineVersion { EngineName = "A180 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1332, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.0m, TopSpeedKmh = 205, FuelConsumptionCombined = 5.7m },
                new EngineVersion { EngineName = "A200 163 KM", PowerHP = 163, PowerKW = 120, Displacement = 1332, FuelTypeId = mild,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 220, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "A45 S AMG 421 KM", PowerHP = 421, PowerKW = 310, Displacement = 1991, FuelTypeId = ben,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 3.9m, TopSpeedKmh = 270, FuelConsumptionCombined = 8.7m },
                new EngineVersion { EngineName = "A180d 116 KM", PowerHP = 116, PowerKW = 85, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 260, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.4m, TopSpeedKmh = 200, FuelConsumptionCombined = 4.1m },
                new EngineVersion { EngineName = "A200d 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1950, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.3m, TopSpeedKmh = 219, FuelConsumptionCombined = 4.3m },
            ]);

            int klasaB = GetOrCreateModel(bId, "Klasa B", "mb-klasa-b");
            AddOrReplaceEngines(GetOrFixGeneration(klasaB, "W246 (2011–2018)", "mb-b-w246", 2011, 2018), 90, [
                new EngineVersion { EngineName = "B180 122 KM", PowerHP = 122, PowerKW = 90, Displacement = 1595, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.2m, TopSpeedKmh = 200, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "B200 156 KM", PowerHP = 156, PowerKW = 115, Displacement = 1595, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.6m, TopSpeedKmh = 215, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "B180d 109 KM", PowerHP = 109, PowerKW = 80, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 260, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.7m, TopSpeedKmh = 190, FuelConsumptionCombined = 3.9m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(klasaB, "W247 (2018–)", "mb-b-w247", 2018, null), 100, [
                new EngineVersion { EngineName = "B180 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1332, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.2m, TopSpeedKmh = 205, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "B200 163 KM", PowerHP = 163, PowerKW = 120, Displacement = 1332, FuelTypeId = mild,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.2m, TopSpeedKmh = 218, FuelConsumptionCombined = 6.1m },
                new EngineVersion { EngineName = "B180d 116 KM", PowerHP = 116, PowerKW = 85, Displacement = 1461, FuelTypeId = die,
                    TorqueNm = 260, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.6m, TopSpeedKmh = 200, FuelConsumptionCombined = 4.2m },
            ]);

            int cla = GetOrCreateModel(bId, "CLA", "mb-cla");
            AddOrReplaceEngines(GetOrFixGeneration(cla, "C117 (2013–2019)", "mb-cla-c117", 2013, 2019), 100, [
                new EngineVersion { EngineName = "CLA180 122 KM", PowerHP = 122, PowerKW = 90, Displacement = 1595, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.0m, TopSpeedKmh = 202, FuelConsumptionCombined = 5.8m },
                new EngineVersion { EngineName = "CLA250 211 KM", PowerHP = 211, PowerKW = 155, Displacement = 1991, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.7m, TopSpeedKmh = 240, FuelConsumptionCombined = 7.1m },
                new EngineVersion { EngineName = "CLA45 AMG 381 KM", PowerHP = 381, PowerKW = 280, Displacement = 1991, FuelTypeId = ben,
                    TorqueNm = 475, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 4.2m, TopSpeedKmh = 270, FuelConsumptionCombined = 7.3m },
                new EngineVersion { EngineName = "CLA200d 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 2143, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 213, FuelConsumptionCombined = 4.2m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(cla, "C118 (2019–)", "mb-cla-c118", 2019, null), 110, [
                new EngineVersion { EngineName = "CLA180 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 1332, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.2m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "CLA250 224 KM", PowerHP = 224, PowerKW = 165, Displacement = 1991, FuelTypeId = mild,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.5m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.0m },
                new EngineVersion { EngineName = "CLA45 S AMG 421 KM", PowerHP = 421, PowerKW = 310, Displacement = 1991, FuelTypeId = ben,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 4.0m, TopSpeedKmh = 270, FuelConsumptionCombined = 8.7m },
                new EngineVersion { EngineName = "CLA200d 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1950, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.7m, TopSpeedKmh = 222, FuelConsumptionCombined = 4.4m },
            ]);

            int cls = GetOrCreateModel(bId, "CLS", "mb-cls");
            AddOrReplaceEngines(GetOrFixGeneration(cls, "C218 (2010–2017)", "mb-cls-c218", 2010, 2017), 180, [
                new EngineVersion { EngineName = "CLS350 306 KM", PowerHP = 306, PowerKW = 225, Displacement = 3498, FuelTypeId = ben,
                    TorqueNm = 370, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 6.1m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "CLS63 AMG 557 KM", PowerHP = 557, PowerKW = 410, Displacement = 5461, FuelTypeId = ben,
                    TorqueNm = 720, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.3m, TopSpeedKmh = 250, FuelConsumptionCombined = 10.6m },
                new EngineVersion { EngineName = "CLS350d 258 KM", PowerHP = 258, PowerKW = 190, Displacement = 2987, FuelTypeId = die,
                    TorqueNm = 620, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 6.3m, TopSpeedKmh = 250, FuelConsumptionCombined = 5.9m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(cls, "C257 (2018–2023)", "mb-cls-c257", 2018, 2023), 250, [
                new EngineVersion { EngineName = "CLS350 299 KM", PowerHP = 299, PowerKW = 220, Displacement = 1991, FuelTypeId = mild,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 6.0m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "CLS450 4Matic 367 KM", PowerHP = 367, PowerKW = 270, Displacement = 2999, FuelTypeId = mild,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 4.8m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.4m },
                new EngineVersion { EngineName = "CLS300d 245 KM", PowerHP = 245, PowerKW = 180, Displacement = 1950, FuelTypeId = die,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 6.9m, TopSpeedKmh = 245, FuelConsumptionCombined = 5.2m },
                new EngineVersion { EngineName = "CLS400d 4Matic 340 KM", PowerHP = 340, PowerKW = 250, Displacement = 2925, FuelTypeId = die,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.0m, TopSpeedKmh = 250, FuelConsumptionCombined = 6.2m },
            ]);

            int gla = GetOrCreateModel(bId, "GLA", "mb-gla");
            AddOrReplaceEngines(GetOrFixGeneration(gla, "X156 (2013–2020)", "mb-gla-x156", 2013, 2020), 100, [
                new EngineVersion { EngineName = "GLA180 122 KM", PowerHP = 122, PowerKW = 90, Displacement = 1595, FuelTypeId = ben,
                    TorqueNm = 200, EuroNorm = "Euro 6", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.7m, TopSpeedKmh = 195, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "GLA250 4Matic 211 KM", PowerHP = 211, PowerKW = 155, Displacement = 1991, FuelTypeId = ben,
                    TorqueNm = 350, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.9m, TopSpeedKmh = 230, FuelConsumptionCombined = 7.5m },
                new EngineVersion { EngineName = "GLA45 AMG 381 KM", PowerHP = 381, PowerKW = 280, Displacement = 1991, FuelTypeId = ben,
                    TorqueNm = 475, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 4.4m, TopSpeedKmh = 250, FuelConsumptionCombined = 7.9m },
                new EngineVersion { EngineName = "GLA200d 136 KM", PowerHP = 136, PowerKW = 100, Displacement = 2143, FuelTypeId = die,
                    TorqueNm = 300, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.3m, TopSpeedKmh = 205, FuelConsumptionCombined = 4.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(gla, "H247 (2020–)", "mb-gla-h247", 2020, null), 110, [
                new EngineVersion { EngineName = "GLA200 163 KM", PowerHP = 163, PowerKW = 120, Displacement = 1332, FuelTypeId = mild,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.7m, TopSpeedKmh = 213, FuelConsumptionCombined = 6.3m },
                new EngineVersion { EngineName = "GLA250 4Matic 224 KM", PowerHP = 224, PowerKW = 165, Displacement = 1991, FuelTypeId = mild,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.6m, TopSpeedKmh = 235, FuelConsumptionCombined = 7.4m },
                new EngineVersion { EngineName = "GLA45 S AMG 421 KM", PowerHP = 421, PowerKW = 310, Displacement = 1991, FuelTypeId = ben,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 4.4m, TopSpeedKmh = 270, FuelConsumptionCombined = 8.8m },
                new EngineVersion { EngineName = "GLA200d 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1950, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.0m, TopSpeedKmh = 213, FuelConsumptionCombined = 4.6m },
            ]);

            int glb = GetOrCreateModel(bId, "GLB", "mb-glb");
            AddOrReplaceEngines(GetOrFixGeneration(glb, "X247 (2019–)", "mb-glb-x247", 2019, null), 110, [
                new EngineVersion { EngineName = "GLB200 163 KM", PowerHP = 163, PowerKW = 120, Displacement = 1332, FuelTypeId = mild,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.9m, TopSpeedKmh = 205, FuelConsumptionCombined = 6.6m },
                new EngineVersion { EngineName = "GLB250 4Matic 224 KM", PowerHP = 224, PowerKW = 165, Displacement = 1991, FuelTypeId = mild,
                    TorqueNm = 350, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.9m, TopSpeedKmh = 232, FuelConsumptionCombined = 7.7m },
                new EngineVersion { EngineName = "GLB200d 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1950, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.3m, TopSpeedKmh = 205, FuelConsumptionCombined = 5.0m },
                new EngineVersion { EngineName = "GLB220d 4Matic 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1950, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.7m, TopSpeedKmh = 213, FuelConsumptionCombined = 5.6m },
            ]);

            int glc = GetOrCreateModel(bId, "GLC", "mb-glc");
            AddOrReplaceEngines(GetOrFixGeneration(glc, "X253 (2015–2022)", "mb-glc-x253", 2015, 2022), 130, [
                new EngineVersion { EngineName = "GLC200 197 KM", PowerHP = 197, PowerKW = 145, Displacement = 1991, FuelTypeId = mild,
                    TorqueNm = 320, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.0m, TopSpeedKmh = 213, FuelConsumptionCombined = 8.0m },
                new EngineVersion { EngineName = "GLC300 258 KM", PowerHP = 258, PowerKW = 190, Displacement = 1991, FuelTypeId = mild,
                    TorqueNm = 370, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.2m, TopSpeedKmh = 240, FuelConsumptionCombined = 8.3m },
                new EngineVersion { EngineName = "GLC63 AMG S 510 KM", PowerHP = 510, PowerKW = 375, Displacement = 3982, FuelTypeId = ben,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 3.8m, TopSpeedKmh = 280, FuelConsumptionCombined = 11.7m },
                new EngineVersion { EngineName = "GLC220d 194 KM", PowerHP = 194, PowerKW = 143, Displacement = 2143, FuelTypeId = die,
                    TorqueNm = 400, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.3m, TopSpeedKmh = 205, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "GLC300d 245 KM", PowerHP = 245, PowerKW = 180, Displacement = 1950, FuelTypeId = die,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.8m, TopSpeedKmh = 233, FuelConsumptionCombined = 5.9m },
            ]);

            int gle = GetOrCreateModel(bId, "GLE", "mb-gle");
            AddOrReplaceEngines(GetOrFixGeneration(gle, "W166 (2015–2019)", "mb-gle-w166", 2015, 2019), 200, [
                new EngineVersion { EngineName = "GLE350d 258 KM", PowerHP = 258, PowerKW = 190, Displacement = 2987, FuelTypeId = die,
                    TorqueNm = 620, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 7.4m, TopSpeedKmh = 210, FuelConsumptionCombined = 6.9m },
                new EngineVersion { EngineName = "GLE43 AMG 367 KM", PowerHP = 367, PowerKW = 270, Displacement = 2996, FuelTypeId = ben,
                    TorqueNm = 520, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.7m, TopSpeedKmh = 250, FuelConsumptionCombined = 9.7m },
                new EngineVersion { EngineName = "GLE63 AMG S 585 KM", PowerHP = 585, PowerKW = 430, Displacement = 5461, FuelTypeId = ben,
                    TorqueNm = 760, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.2m, TopSpeedKmh = 250, FuelConsumptionCombined = 12.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(gle, "V167 (2019–)", "mb-gle-v167", 2019, null), 250, [
                new EngineVersion { EngineName = "GLE350 e PHEV 320 KM", PowerHP = 320, PowerKW = 235, Displacement = 1991, FuelTypeId = phev,
                    TorqueNm = 550, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 6.0m, TopSpeedKmh = 235, FuelConsumptionCombined = 2.1m },
                new EngineVersion { EngineName = "GLE450 4Matic 367 KM", PowerHP = 367, PowerKW = 270, Displacement = 2999, FuelTypeId = mild,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.7m, TopSpeedKmh = 240, FuelConsumptionCombined = 9.4m },
                new EngineVersion { EngineName = "AMG GLE63 S 612 KM", PowerHP = 612, PowerKW = 450, Displacement = 3982, FuelTypeId = mild,
                    TorqueNm = 850, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 3.8m, TopSpeedKmh = 250, FuelConsumptionCombined = 12.7m },
                new EngineVersion { EngineName = "GLE300d 245 KM", PowerHP = 245, PowerKW = 180, Displacement = 1950, FuelTypeId = die,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 7.6m, TopSpeedKmh = 213, FuelConsumptionCombined = 6.6m },
                new EngineVersion { EngineName = "GLE400d 4Matic 330 KM", PowerHP = 330, PowerKW = 243, Displacement = 2925, FuelTypeId = die,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 5.8m, TopSpeedKmh = 240, FuelConsumptionCombined = 7.0m },
            ]);

            int gls = GetOrCreateModel(bId, "GLS", "mb-gls");
            AddOrReplaceEngines(GetOrFixGeneration(gls, "X166 (2015–2019)", "mb-gls-x166", 2015, 2019), 250, [
                new EngineVersion { EngineName = "GLS350d 258 KM", PowerHP = 258, PowerKW = 190, Displacement = 2987, FuelTypeId = die,
                    TorqueNm = 620, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 8.0m, TopSpeedKmh = 210, FuelConsumptionCombined = 7.6m },
                new EngineVersion { EngineName = "GLS400 367 KM", PowerHP = 367, PowerKW = 270, Displacement = 2996, FuelTypeId = ben,
                    TorqueNm = 500, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.3m, TopSpeedKmh = 240, FuelConsumptionCombined = 9.9m },
                new EngineVersion { EngineName = "GLS63 AMG 585 KM", PowerHP = 585, PowerKW = 430, Displacement = 5461, FuelTypeId = ben,
                    TorqueNm = 760, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.6m, TopSpeedKmh = 250, FuelConsumptionCombined = 13.0m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(gls, "X167 (2019–)", "mb-gls-x167", 2019, null), 280, [
                new EngineVersion { EngineName = "GLS450 4Matic 367 KM", PowerHP = 367, PowerKW = 270, Displacement = 2999, FuelTypeId = mild,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.1m, TopSpeedKmh = 240, FuelConsumptionCombined = 9.7m },
                new EngineVersion { EngineName = "AMG GLS63 612 KM", PowerHP = 612, PowerKW = 450, Displacement = 3982, FuelTypeId = mild,
                    TorqueNm = 850, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.2m, TopSpeedKmh = 250, FuelConsumptionCombined = 13.0m },
                new EngineVersion { EngineName = "GLS400d 4Matic 330 KM", PowerHP = 330, PowerKW = 243, Displacement = 2925, FuelTypeId = die,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.6m, TopSpeedKmh = 240, FuelConsumptionCombined = 7.7m },
            ]);

            int gKlasa = GetOrCreateModel(bId, "Klasa G", "mb-klasa-g");
            AddOrReplaceEngines(GetOrFixGeneration(gKlasa, "W463 (1990–2018)", "mb-g-w463", 1990, 2018), 150, [
                new EngineVersion { EngineName = "G350d 211 KM", PowerHP = 211, PowerKW = 155, Displacement = 2987, FuelTypeId = die,
                    TorqueNm = 540, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 9.0m, TopSpeedKmh = 175, FuelConsumptionCombined = 10.6m },
                new EngineVersion { EngineName = "G500 422 KM", PowerHP = 422, PowerKW = 310, Displacement = 3982, FuelTypeId = ben,
                    TorqueNm = 610, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 5.9m, TopSpeedKmh = 210, FuelConsumptionCombined = 13.1m },
                new EngineVersion { EngineName = "G63 AMG 585 KM", PowerHP = 585, PowerKW = 430, Displacement = 5461, FuelTypeId = ben,
                    TorqueNm = 850, EuroNorm = "Euro 6", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.5m, TopSpeedKmh = 220, FuelConsumptionCombined = 13.8m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(gKlasa, "W463 II (2018–)", "mb-g-w463ii", 2018, null), 300, [
                new EngineVersion { EngineName = "G500 449 KM", PowerHP = 449, PowerKW = 330, Displacement = 3982, FuelTypeId = mild,
                    TorqueNm = 610, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 5.9m, TopSpeedKmh = 210, FuelConsumptionCombined = 12.5m },
                new EngineVersion { EngineName = "G63 AMG 585 KM", PowerHP = 585, PowerKW = 430, Displacement = 3982, FuelTypeId = mild,
                    TorqueNm = 850, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 4.5m, TopSpeedKmh = 220, FuelConsumptionCombined = 13.1m },
                new EngineVersion { EngineName = "G400d 330 KM", PowerHP = 330, PowerKW = 243, Displacement = 2925, FuelTypeId = die,
                    TorqueNm = 700, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 6.4m, TopSpeedKmh = 210, FuelConsumptionCombined = 9.9m },
            ]);

            int amgGt = GetOrCreateModel(bId, "AMG GT", "mb-amg-gt");
            AddOrReplaceEngines(GetOrFixGeneration(amgGt, "C190 (2014–2023)", "mb-amg-gt-c190", 2014, 2023), 450, [
                new EngineVersion { EngineName = "AMG GT 476 KM", PowerHP = 476, PowerKW = 350, Displacement = 3982, FuelTypeId = ben,
                    TorqueNm = 630, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 4.0m, TopSpeedKmh = 304, FuelConsumptionCombined = 11.4m },
                new EngineVersion { EngineName = "AMG GT S 522 KM", PowerHP = 522, PowerKW = 384, Displacement = 3982, FuelTypeId = ben,
                    TorqueNm = 650, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.8m, TopSpeedKmh = 310, FuelConsumptionCombined = 11.5m },
                new EngineVersion { EngineName = "AMG GT R 585 KM", PowerHP = 585, PowerKW = 430, Displacement = 3982, FuelTypeId = ben,
                    TorqueNm = 700, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.6m, TopSpeedKmh = 318, FuelConsumptionCombined = 11.4m },
                new EngineVersion { EngineName = "AMG GT Black Series 730 KM", PowerHP = 730, PowerKW = 537, Displacement = 3982, FuelTypeId = ben,
                    TorqueNm = 800, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 8, Acceleration0100 = 3.2m, TopSpeedKmh = 325, FuelConsumptionCombined = 12.8m },
            ]);

            int klasaS = GetOrCreateModel(bId, "Klasa S", "mb-klasa-s");
            PrepareGenerations(klasaS,
                ("W222 (2013–2020)", "mb-s-w222", 2013, 2020),
                ("W223 (2020–)", "mb-s-w223", 2020, null));
            AddOrReplaceEngines(GetOrFixGeneration(klasaS, "W222 (2013–2020)", "mb-s-w222", 2013, 2020), 250, [
                new EngineVersion { EngineName = "S350d 286 KM", PowerHP = 286, PowerKW = 210, Displacement = 2925, FuelTypeId = mild,
                    TorqueNm = 600, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 6.0m, TopSpeedKmh = 250, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "S500 435 KM", PowerHP = 435, PowerKW = 320, Displacement = 2999, FuelTypeId = ben,
                    TorqueNm = 520, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 4.9m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "S63 AMG 612 KM", PowerHP = 612, PowerKW = 450, Displacement = 3982, FuelTypeId = ben,
                    TorqueNm = 900, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 8, Acceleration0100 = 3.6m, TopSpeedKmh = 300, FuelConsumptionCombined = 11.5m },
            ]);
            AddOrReplaceEngines(GetOrFixGeneration(klasaS, "W223 (2020–)", "mb-s-w223", 2020, null), 280, [
                new EngineVersion { EngineName = "S350d 286 KM", PowerHP = 286, PowerKW = 210, Displacement = 2925, FuelTypeId = mild,
                    TorqueNm = 600, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "RWD",
                    Cylinders = 6, Acceleration0100 = 6.0m, TopSpeedKmh = 250, FuelConsumptionCombined = 6.2m },
                new EngineVersion { EngineName = "S500 435 KM", PowerHP = 435, PowerKW = 320, Displacement = 2999, FuelTypeId = mild,
                    TorqueNm = 520, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 4.9m, TopSpeedKmh = 250, FuelConsumptionCombined = 8.7m },
                new EngineVersion { EngineName = "S580e PHEV 510 KM", PowerHP = 510, PowerKW = 375, Displacement = 2999, FuelTypeId = phev,
                    TorqueNm = 750, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "AWD",
                    Cylinders = 6, Acceleration0100 = 4.4m, TopSpeedKmh = 250, FuelConsumptionCombined = 1.9m },
            ]);

            int eqa = GetOrCreateModel(bId, "EQA", "mb-eqa");
            AddOrReplaceEngines(GetOrFixGeneration(eqa, "H243 (2021–)", "mb-eqa-h243", 2021, null), 150, [
                new EngineVersion { EngineName = "EQA250 66.5 kWh 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 385, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 0, Acceleration0100 = 8.9m, TopSpeedKmh = 160, FuelConsumptionCombined = 15.7m },
                new EngineVersion { EngineName = "EQA350 4MATIC 292 KM", PowerHP = 292, PowerKW = 215, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 520, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 6.0m, TopSpeedKmh = 160, FuelConsumptionCombined = 18.1m },
            ]);

            int eqb = GetOrCreateModel(bId, "EQB", "mb-eqb");
            AddOrReplaceEngines(GetOrFixGeneration(eqb, "X243 (2021–)", "mb-eqb-x243", 2021, null), 150, [
                new EngineVersion { EngineName = "EQB250 66.5 kWh 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 385, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "FWD",
                    Cylinders = 0, Acceleration0100 = 9.2m, TopSpeedKmh = 160, FuelConsumptionCombined = 17.7m },
                new EngineVersion { EngineName = "EQB350 4MATIC 292 KM", PowerHP = 292, PowerKW = 215, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 520, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 6.2m, TopSpeedKmh = 160, FuelConsumptionCombined = 19.7m },
            ]);

            int eqc = GetOrCreateModel(bId, "EQC", "mb-eqc");
            AddOrReplaceEngines(GetOrFixGeneration(eqc, "N293 (2019–2023)", "mb-eqc-n293", 2019, 2023), 350, [
                new EngineVersion { EngineName = "EQC400 4MATIC 408 KM", PowerHP = 408, PowerKW = 300, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 760, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 5.1m, TopSpeedKmh = 180, FuelConsumptionCombined = 21.3m },
            ]);

            int eqe = GetOrCreateModel(bId, "EQE", "mb-eqe");
            AddOrReplaceEngines(GetOrFixGeneration(eqe, "V295 (2022–)", "mb-eqe-v295", 2022, null), 250, [
                new EngineVersion { EngineName = "EQE350 90.6 kWh 292 KM", PowerHP = 292, PowerKW = 215, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 550, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "RWD",
                    Cylinders = 0, Acceleration0100 = 6.4m, TopSpeedKmh = 210, FuelConsumptionCombined = 16.8m },
                new EngineVersion { EngineName = "AMG EQE 43 4MATIC 476 KM", PowerHP = 476, PowerKW = 350, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 858, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 4.2m, TopSpeedKmh = 210, FuelConsumptionCombined = 19.7m },
            ]);

            int eqs = GetOrCreateModel(bId, "EQS", "mb-eqs");
            AddOrReplaceEngines(GetOrFixGeneration(eqs, "V297 (2021–)", "mb-eqs-v297", 2021, null), 300, [
                new EngineVersion { EngineName = "EQS450+ 108.4 kWh 333 KM", PowerHP = 333, PowerKW = 245, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 568, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "RWD",
                    Cylinders = 0, Acceleration0100 = 6.2m, TopSpeedKmh = 210, FuelConsumptionCombined = 17.4m },
                new EngineVersion { EngineName = "AMG EQS 53 4MATIC+ 761 KM", PowerHP = 761, PowerKW = 560, Displacement = 0, FuelTypeId = ev,
                    TorqueNm = 1020, EuroNorm = "Euro 6d", GearboxType = "eAutomatic", DriveType = "AWD",
                    Cylinders = 0, Acceleration0100 = 3.4m, TopSpeedKmh = 250, FuelConsumptionCombined = 21.8m },
            ]);

            int sprinter = GetOrCreateModel(bId, "Sprinter", "mb-sprinter");
            AddOrReplaceEngines(GetOrFixGeneration(sprinter, "W907 (2018–)", "mb-sprinter-w907", 2018, null), 100, [
                new EngineVersion { EngineName = "313 CDI 2.0 130 KM", PowerHP = 130, PowerKW = 96, Displacement = 1950, FuelTypeId = die,
                    TorqueNm = 330, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 16.0m, TopSpeedKmh = 155, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "319 CDI 2.0 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1950, FuelTypeId = die,
                    TorqueNm = 440, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 13.0m, TopSpeedKmh = 165, FuelConsumptionCombined = 9.5m },
            ]);

            int vito = GetOrCreateModel(bId, "Vito", "mb-vito");
            AddOrReplaceEngines(GetOrFixGeneration(vito, "W447 (2014–)", "mb-vito-w447", 2014, null), 90, [
                new EngineVersion { EngineName = "111 CDI 2.0 114 KM", PowerHP = 114, PowerKW = 84, Displacement = 1598, FuelTypeId = die,
                    TorqueNm = 270, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 15.5m, TopSpeedKmh = 168, FuelConsumptionCombined = 6.8m },
                new EngineVersion { EngineName = "119 CDI 2.0 190 KM", PowerHP = 190, PowerKW = 140, Displacement = 1950, FuelTypeId = die,
                    TorqueNm = 440, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 9.5m, TopSpeedKmh = 205, FuelConsumptionCombined = 7.3m },
            ]);

            int vKlasa = GetOrCreateModel(bId, "Klasa V", "mb-klasa-v");
            AddOrReplaceEngines(GetOrFixGeneration(vKlasa, "W447 (2014–)", "mb-v-w447", 2014, null), 100, [
                new EngineVersion { EngineName = "V220d 163 KM", PowerHP = 163, PowerKW = 120, Displacement = 2143, FuelTypeId = die,
                    TorqueNm = 380, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.9m, TopSpeedKmh = 190, FuelConsumptionCombined = 6.9m },
                new EngineVersion { EngineName = "V300d 239 KM", PowerHP = 239, PowerKW = 176, Displacement = 1950, FuelTypeId = die,
                    TorqueNm = 500, EuroNorm = "Euro 6d", GearboxType = "automatic", DriveType = "AWD",
                    Cylinders = 4, Acceleration0100 = 8.4m, TopSpeedKmh = 214, FuelConsumptionCombined = 7.8m },
            ]);
        }

        logger.LogInformation("[ComprehensiveSeeder] Completed seeding premium cars, motorcycles, trucks.");

        // Audit: list every generation whose name still looks like scraper/import placeholder
        // junk (e.g. "Generation I (2011–2022)", same pattern Bugatti Chiron and Lamborghini
        // Aventador had). These models were never given a dedicated ComprehensiveSeeder entry,
        // so whatever generic small-engine data they were originally imported with is still
        // live. This turns "which models are still broken" from a screenshot-by-screenshot hunt
        // into one log line to search for.
        var remainingGeneric = db.Generations.Include(g => g.Model).ThenInclude(m => m.Brand)
            .AsEnumerable()
            .Where(g => IsGenericGenName(g.Name))
            .Select(g => $"{g.Model.Brand.Name} {g.Model.Name} — \"{g.Name}\" (genId={g.Id})")
            .OrderBy(s => s)
            .ToList();
        if (remainingGeneric.Any())
            logger.LogWarning(
                "[STARTUP-TRACE] AUDIT: {Count} generations still have placeholder names (likely still showing wrong/generic engine data): {List}",
                remainingGeneric.Count, string.Join(" | ", remainingGeneric));
        else
            logger.LogInformation("[STARTUP-TRACE] AUDIT: no generations with placeholder names remain.");
    }
}
