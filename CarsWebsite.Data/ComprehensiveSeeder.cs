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
        var brandDict = db.Brands.Include(b => b.Categories).ToDictionary(b => b.Name, b => b);
        var fuelDict  = db.FuelTypes.ToDictionary(f => f.Name, f => f.Id);
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

        // Like GetOrCreateGeneration, but if the model has exactly one generation with a generic
        // placeholder name (e.g. "Generation I"), renames it in place to preserve existing FK refs.
        int GetOrFixGeneration(int modelId, string name, string slug, int yearFrom, int? yearTo)
        {
            var g = db.Generations.FirstOrDefault(x => x.ModelId == modelId && x.Name == name);
            if (g != null) return g.Id;

            var allGens = db.Generations.Where(x => x.ModelId == modelId).ToList();
            if (allGens.Count == 1)
            {
                var sole = allGens[0];
                bool isGeneric = sole.Name is "Generation I" or "Gen 1" or "I" or "Gen I"
                    || sole.Name.StartsWith("Gen ", StringComparison.OrdinalIgnoreCase);
                if (isGeneric)
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
            if (db.EngineVersions.Any(e => e.GenerationId == generationId && e.TorqueNm != null)) return;
            foreach (var e in engines) e.GenerationId = generationId;
            db.EngineVersions.AddRange(engines);
            db.SaveChanges();
        }

        // Removes engines with HP below minExpectedHp (wrong data for this brand), then adds correct ones.
        void AddOrReplaceEngines(int generationId, int minExpectedHp, List<EngineVersion> engines)
        {
            var wrong = db.EngineVersions
                .Where(e => e.GenerationId == generationId && e.PowerHP < minExpectedHp)
                .ToList();
            if (wrong.Any())
            {
                db.EngineVersions.RemoveRange(wrong);
                db.SaveChanges();
                logger.LogInformation("[ComprehensiveSeeder] Removed {Count} wrong engines (<{Min}HP) from gen {Id}", wrong.Count, minExpectedHp, generationId);
            }
            if (db.EngineVersions.Any(e => e.GenerationId == generationId && e.TorqueNm != null)) return;
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
            AddOrReplaceEngines(GetOrFixGeneration(veyron, "16.4 (2005–2015)", "bugatti-veyron-164", 2005, 2015), 900, [
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
            AddOrReplaceEngines(GetOrFixGeneration(chiron, "Chiron (2016–2023)", "bugatti-chiron-2016", 2016, 2023), 900, [
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
            AddOrReplaceEngines(GetOrFixGeneration(bolide, "Bolide (2024–)", "bugatti-bolide-2024", 2024, null), 900, [
                new EngineVersion { EngineName = "W16 8.0 1825 KM", PowerHP = 1825, PowerKW = 1342, Displacement = 7993, FuelTypeId = ben,
                    TorqueNm = 1850, EuroNorm = "Euro 6d", GearboxType = "dsg",
                    DriveType = "AWD", Cylinders = 16, Acceleration0100 = 2.2m, TopSpeedKmh = 500,
                    FuelConsumptionCombined = 24.0m },
            ]);
        }

        // ── ROLLS-ROYCE ─────────────────────────────────────────────────────────
        {
            int bId = GetOrCreateBrand("Rolls-Royce", "rolls-royce", "auta-osobowe");
            if (BrandNeedsModels(bId)) { seededModelBrandIds.Add(bId); }

            int ghost = GetOrCreateModel(bId, "Ghost", "rr-ghost");
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
        }

        logger.LogInformation("[ComprehensiveSeeder] Completed seeding premium cars, motorcycles, trucks.");
    }
}
