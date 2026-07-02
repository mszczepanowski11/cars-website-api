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
        int ben  = GetFuel("Benzyna");
        int die  = GetFuel("Diesel");
        int hyb  = GetFuel("Hybryda");
        int phev = GetFuel("Hybryda plug-in");
        int ev   = GetFuel("Elektryczny");
        int lpg  = GetFuel("LPG");
        int mild = GetFuel("Hybryda mild");

        // A missing fuel-type name must not crash this seeder via an FK violation on
        // EngineVersion.FuelTypeId — fall back to Benzyna and log loudly instead.
        if (ben == 0 && fuelDict.Count > 0) ben = fuelDict.Values.First();
        if (die == 0) { logger.LogError("[STARTUP-TRACE] ComprehensiveSeeder: FuelType 'Diesel' missing — falling back to Benzyna"); die = ben; }
        if (hyb == 0) { logger.LogError("[STARTUP-TRACE] ComprehensiveSeeder: FuelType 'Hybryda' missing — falling back to Benzyna"); hyb = ben; }
        if (phev == 0) { logger.LogError("[STARTUP-TRACE] ComprehensiveSeeder: FuelType 'Hybryda plug-in' missing — falling back to Benzyna"); phev = ben; }
        if (ev == 0) { logger.LogError("[STARTUP-TRACE] ComprehensiveSeeder: FuelType 'Elektryczny' missing — falling back to Benzyna"); ev = ben; }
        if (lpg == 0) { logger.LogError("[STARTUP-TRACE] ComprehensiveSeeder: FuelType 'LPG' missing — falling back to Benzyna"); lpg = ben; }
        if (mild == 0) { logger.LogError("[STARTUP-TRACE] ComprehensiveSeeder: FuelType 'Hybryda mild' missing — falling back to Benzyna"); mild = ben; }

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

        int GetOrCreateGeneration(int modelId, string name, string slug, int yearFrom, int? yearTo)
        {
            var g = db.Generations.FirstOrDefault(x => x.ModelId == modelId && x.Name == name);
            if (g != null) return g.Id;
            g = new Generation { ModelId = modelId, Name = name, Slug = slug, YearFrom = yearFrom, YearTo = yearTo };
            db.Generations.Add(g);
            db.SaveChanges();
            return g.Id;
        }

        static bool IsGenericGenName(string n) =>
            n is "Generation I" or "Generation II" or "Generation III" or "Generation IV" or "Generation V"
            or "Gen 1" or "Gen 2" or "Gen 3" or "Gen 4"
            or "I" or "II" or "III" or "IV" or "Gen I" or "Gen II" or "Gen III" or "Gen IV"
            || n.StartsWith("Gen ", StringComparison.OrdinalIgnoreCase);

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
                // Nullify FK in CarAdverts before deleting — the migration defined this FK without ON DELETE,
                // so MySQL defaults to RESTRICT and would block the delete otherwise.
                db.Database.ExecuteSqlRaw($"UPDATE `CarAdverts` SET `GenerationId` = NULL WHERE `GenerationId` = {orphan.Id}");
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
                        // so MySQL defaults to RESTRICT and would block the delete otherwise.
                        db.Database.ExecuteSqlRaw($"UPDATE `CarAdverts` SET `GenerationId` = NULL WHERE `GenerationId` = {o.Id}");
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
                db.EngineVersions.RemoveRange(wrongNames);
                db.SaveChanges();
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
                db.EngineVersions.RemoveRange(wrong);
                db.SaveChanges();
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
                db.EngineVersions.RemoveRange(existing);
                db.SaveChanges();
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
            AddEngines(GetOrCreateGeneration(m147, "I (2000–2010)", "alfa-147-i", 2000, 2010), [
                new EngineVersion { EngineName = "1.4 TS 90 KM", PowerHP = 90, PowerKW = 66, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 127, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 12.5m, TopSpeedKmh = 177, FuelConsumptionCombined = 7.8m },
                new EngineVersion { EngineName = "1.6 TS 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 144, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.5m, TopSpeedKmh = 190, FuelConsumptionCombined = 8.2m },
                new EngineVersion { EngineName = "2.0 TS 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1970, FuelTypeId = ben,
                    TorqueNm = 187, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.2m, TopSpeedKmh = 215, FuelConsumptionCombined = 9.5m },
                new EngineVersion { EngineName = "GTA 3.2 V6 250 KM", PowerHP = 250, PowerKW = 184, Displacement = 3179, FuelTypeId = ben,
                    TorqueNm = 300, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 6.3m, TopSpeedKmh = 250, FuelConsumptionCombined = 13.5m },
                new EngineVersion { EngineName = "1.9 JTD 100 KM", PowerHP = 100, PowerKW = 74, Displacement = 1910, FuelTypeId = die,
                    TorqueNm = 200, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 11.8m, TopSpeedKmh = 185, FuelConsumptionCombined = 5.6m },
                new EngineVersion { EngineName = "1.9 JTD 115 KM", PowerHP = 115, PowerKW = 85, Displacement = 1910, FuelTypeId = die,
                    TorqueNm = 270, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.8m, TopSpeedKmh = 193, FuelConsumptionCombined = 5.4m },
                new EngineVersion { EngineName = "1.9 JTD 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1910, FuelTypeId = die,
                    TorqueNm = 305, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.9m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.8m },
            ]);

            int m156 = GetOrCreateModel(bId, "156", "alfa-romeo-156");
            AddEngines(GetOrCreateGeneration(m156, "932 (1997–2007)", "alfa-156-932", 1997, 2007), [
                new EngineVersion { EngineName = "1.6 TS 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1598, FuelTypeId = ben,
                    TorqueNm = 144, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.9m, TopSpeedKmh = 195, FuelConsumptionCombined = 8.5m },
                new EngineVersion { EngineName = "1.8 TS 144 KM", PowerHP = 144, PowerKW = 106, Displacement = 1747, FuelTypeId = ben,
                    TorqueNm = 172, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.4m, TopSpeedKmh = 210, FuelConsumptionCombined = 9.2m },
                new EngineVersion { EngineName = "2.0 TS 155 KM", PowerHP = 155, PowerKW = 114, Displacement = 1970, FuelTypeId = ben,
                    TorqueNm = 187, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.9m, TopSpeedKmh = 215, FuelConsumptionCombined = 9.8m },
                new EngineVersion { EngineName = "2.5 V6 24V 192 KM", PowerHP = 192, PowerKW = 141, Displacement = 2492, FuelTypeId = ben,
                    TorqueNm = 222, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 7.0m, TopSpeedKmh = 230, FuelConsumptionCombined = 11.5m },
                new EngineVersion { EngineName = "GTA 3.2 V6 250 KM", PowerHP = 250, PowerKW = 184, Displacement = 3179, FuelTypeId = ben,
                    TorqueNm = 300, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 6.3m, TopSpeedKmh = 250, FuelConsumptionCombined = 12.8m },
                new EngineVersion { EngineName = "1.9 JTD 105 KM", PowerHP = 105, PowerKW = 77, Displacement = 1910, FuelTypeId = die,
                    TorqueNm = 200, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 10.7m, TopSpeedKmh = 190, FuelConsumptionCombined = 5.5m },
                new EngineVersion { EngineName = "1.9 JTD 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1910, FuelTypeId = die,
                    TorqueNm = 305, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.5m, TopSpeedKmh = 210, FuelConsumptionCombined = 5.7m },
                new EngineVersion { EngineName = "2.4 JTD 175 KM", PowerHP = 175, PowerKW = 129, Displacement = 2387, FuelTypeId = die,
                    TorqueNm = 370, EuroNorm = "Euro 4", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 5, Acceleration0100 = 7.6m, TopSpeedKmh = 220, FuelConsumptionCombined = 6.3m },
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
            AddEngines(GetOrCreateGeneration(focus, "Mk4 (2018–)", "ford-focus-mk4", 2018, null), [
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
            AddEngines(GetOrCreateGeneration(kuga, "Mk2 (2012–2019)", "ford-kuga-mk2", 2012, 2019), [
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
            AddEngines(GetOrCreateGeneration(kuga, "Mk3 (2019–)", "ford-kuga-mk3", 2019, null), [
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

            // ── LAMBORGHINI HURACÁN + URUS + REVUELTO ─────────────────────────────
            int lambId = GetOrCreateBrand("Lamborghini", "lamborghini", "auta-osobowe");
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
            AddEngines(GetOrCreateGeneration(giulietta, "940 (2010–2020)", "alfa-giulietta-940", 2010, 2020), [
                new EngineVersion { EngineName = "1.4 MultiAir Turbo 120 KM", PowerHP = 120, PowerKW = 88, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 215, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 9.8m, TopSpeedKmh = 195, FuelConsumptionCombined = 5.9m },
                new EngineVersion { EngineName = "1.4 MultiAir Turbo 170 KM", PowerHP = 170, PowerKW = 125, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.7m, TopSpeedKmh = 218, FuelConsumptionCombined = 6.0m },
                new EngineVersion { EngineName = "1.75 TBi Quadrifoglio 240 KM", PowerHP = 240, PowerKW = 177, Displacement = 1742, FuelTypeId = ben,
                    TorqueNm = 340, EuroNorm = "Euro 6", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.8m, TopSpeedKmh = 245, FuelConsumptionCombined = 8.1m },
                new EngineVersion { EngineName = "2.0 JTDm 150 KM", PowerHP = 150, PowerKW = 110, Displacement = 1956, FuelTypeId = die,
                    TorqueNm = 320, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 8.8m, TopSpeedKmh = 210, FuelConsumptionCombined = 4.7m },
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
            int ab595 = GetOrCreateModel(abaId, "595", "abarth-595");
            AddEngines(GetOrCreateGeneration(ab595, "312 (2008–)", "abarth-595-312", 2008, null), [
                new EngineVersion { EngineName = "1.4 T-Jet 145 KM", PowerHP = 145, PowerKW = 107, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 206, EuroNorm = "Euro 6d", GearboxType = "manual", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 7.5m, TopSpeedKmh = 215, FuelConsumptionCombined = 6.9m },
                new EngineVersion { EngineName = "1.4 T-Jet Competizione 180 KM", PowerHP = 180, PowerKW = 132, Displacement = 1368, FuelTypeId = ben,
                    TorqueNm = 250, EuroNorm = "Euro 6d", GearboxType = "dsg", DriveType = "FWD",
                    Cylinders = 4, Acceleration0100 = 6.7m, TopSpeedKmh = 225, FuelConsumptionCombined = 7.4m },
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
            AddEngines(GetOrCreateGeneration(pacifica, "RU (2016–)", "chrysler-pacifica-ru", 2016, null), [
                new EngineVersion { EngineName = "3.6 V6 Pentastar 287 KM", PowerHP = 287, PowerKW = 211, Displacement = 3604, FuelTypeId = ben,
                    TorqueNm = 353, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 7.8m, TopSpeedKmh = 195, FuelConsumptionCombined = 12.3m },
                new EngineVersion { EngineName = "3.6 V6 PHEV 260 KM", PowerHP = 260, PowerKW = 191, Displacement = 3604, FuelTypeId = phev,
                    TorqueNm = 353, EuroNorm = "Euro 5", GearboxType = "automatic", DriveType = "FWD",
                    Cylinders = 6, Acceleration0100 = 8.5m, TopSpeedKmh = 185, FuelConsumptionCombined = 3.3m },
            ]);
        }

        // ── Chevrolet ─────────────────────────────────────────────────────────────
        {
            int chevId = GetOrCreateBrand("Chevrolet", "chevrolet", "osobowe");
            int camaro = GetOrCreateModel(chevId, "Camaro", "chevrolet-camaro");
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
        }

        logger.LogInformation("[ComprehensiveSeeder] Completed seeding premium cars, motorcycles, trucks.");
    }
}
