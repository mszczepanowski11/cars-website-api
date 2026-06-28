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
            AddEngines(GetOrCreateGeneration(z900, "Z900 (2017–)", "kawasaki-z900-2017", 2017, null), [
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

            int zx10r = GetOrCreateModel(bId, "Ninja ZX-10R", "kawasaki-zx10r");
            AddEngines(GetOrCreateGeneration(zx10r, "ZX-10R (2021–)", "kawasaki-zx10r-2021", 2021, null), [
                new EngineVersion { EngineName = "998 cc I4 203 KM", PowerHP = 203, PowerKW = 149, Displacement = 998, FuelTypeId = ben,
                    TorqueNm = 115, EuroNorm = "Euro 5", GearboxType = "manual", DriveType = "RWD",
                    Cylinders = 4, Acceleration0100 = 3.0m, TopSpeedKmh = 299, FuelConsumptionCombined = 6.9m },
            ]);
        }

        // ── TRIUMPH (additional models) ──────────────────────────────────────────
        {
            int bId = GetOrCreateBrand("Triumph", "triumph", "motocykle");

            int tiger900 = GetOrCreateModel(bId, "Tiger 900", "triumph-tiger900");
            AddEngines(GetOrCreateGeneration(tiger900, "Tiger 900 (2020–)", "triumph-tiger900-2020", 2020, null), [
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
            AddEngines(GetOrCreateGeneration(bonneville, "T120 (2016–)", "triumph-t120-2016", 2016, null), [
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
            AddEngines(GetOrCreateGeneration(tgx, "TGX (2020–)", "man-tgx-2020", 2020, null), [
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
            AddEngines(GetOrCreateGeneration(tgs, "TGS (2020–)", "man-tgs-2020", 2020, null), [
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

        logger.LogInformation("[ComprehensiveSeeder] Completed seeding premium cars, motorcycles, trucks.");
    }
}
