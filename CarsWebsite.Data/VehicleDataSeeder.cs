using CarsWebsite;
using cars_website_api.CarsWebsite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Data;

/// <summary>
/// Seeds real Polish-market vehicle data with full factory specs:
/// Brand → Model → Generation → EngineVersion (power, torque, CO2, Euro norm, 0-100, top speed, drive type, gearbox, cylinders).
/// Idempotent: skips if any EngineVersion already has TorqueNm populated.
/// </summary>
public static class VehicleDataSeeder
{
    public static void SeedVehicleData(AppDbContext db, ILogger logger)
    {
        // Idempotency guard: if any engine already has TorqueNm set, we already ran.
        if (db.EngineVersions.Any(e => e.TorqueNm != null))
        {
            logger.LogInformation("[VehicleDataSeeder] Already seeded (TorqueNm present) — skipping.");
            return;
        }

        var brandDict = db.Brands.ToDictionary(b => b.Name, b => b.Id);
        var fuelDict  = db.FuelTypes.ToDictionary(f => f.Name, f => f.Id);

        if (!brandDict.Any() || !fuelDict.Any())
        {
            logger.LogWarning("[VehicleDataSeeder] Brands or FuelTypes not yet seeded — skipping.");
            return;
        }

        // Helper: get or create FuelType by name
        int GetOrCreateFuel(string name)
        {
            if (fuelDict.TryGetValue(name, out var id)) return id;
            var ft = new FuelType { Name = name };
            db.FuelTypes.Add(ft);
            db.SaveChanges();
            fuelDict[name] = ft.Id;
            return ft.Id;
        }

        int ben   = GetOrCreateFuel("Benzyna");
        int die   = GetOrCreateFuel("Diesel");
        int hyb   = GetOrCreateFuel("Hybryda");
        int phev  = GetOrCreateFuel("Hybryda PHEV");
        int ev    = GetOrCreateFuel("Elektryczny");
        int lpg   = GetOrCreateFuel("LPG");

        // Helper: get or create Model under a brand
        int GetOrCreateModel(int brandId, string name, string slug)
        {
            var m = db.Models.FirstOrDefault(x => x.BrandId == brandId && x.Name == name);
            if (m != null) return m.Id;
            m = new Model { BrandId = brandId, Name = name, Slug = slug };
            db.Models.Add(m);
            db.SaveChanges();
            return m.Id;
        }

        // Helper: get or create Generation under a model
        int GetOrCreateGeneration(int modelId, string name, string slug, int yearFrom, int? yearTo)
        {
            var g = db.Generations.FirstOrDefault(x => x.ModelId == modelId && x.Name == name);
            if (g != null) return g.Id;
            g = new Generation { ModelId = modelId, Name = name, Slug = slug, YearFrom = yearFrom, YearTo = yearTo };
            db.Generations.Add(g);
            db.SaveChanges();
            return g.Id;
        }

        // Helper: add engine versions to a generation (skips if engines with TorqueNm already exist for that generation)
        void AddEngines(int generationId, List<EngineVersion> engines)
        {
            if (db.EngineVersions.Any(e => e.GenerationId == generationId && e.TorqueNm != null))
                return;
            foreach (var e in engines)
                e.GenerationId = generationId;
            db.EngineVersions.AddRange(engines);
            db.SaveChanges();
        }

        int B(string name) => brandDict.TryGetValue(name, out var id) ? id : 0;

        // ─── VOLKSWAGEN ───────────────────────────────────────────────────────────
        {
            int vw = B("Volkswagen");
            if (vw > 0)
            {
                // Golf — Golf 8
                int golf = GetOrCreateModel(vw, "Golf", "vw-golf");
                int golf8 = GetOrCreateGeneration(golf, "Golf 8", "vw-golf-8", 2019, null);
                AddEngines(golf8, [
                    EV("1.0 TSI 110 KM",  110, 81,  999,  200, 118, "Euro 6d", 5.3m,  10.2m, 197, "FWD", "manual",    3, ben),
                    EV("1.5 TSI 150 KM",  150, 110, 1498, 250, 129, "Euro 6d", 5.7m,   8.5m, 224, "FWD", "manual",    4, ben),
                    EV("2.0 TDI 150 KM",  150, 110, 1968, 360, 121, "Euro 6d", 4.6m,   8.8m, 218, "FWD", "manual",    4, die),
                ]);

                // Passat — B8
                int passat = GetOrCreateModel(vw, "Passat", "vw-passat");
                int passatB8 = GetOrCreateGeneration(passat, "Passat B8", "vw-passat-b8", 2014, 2023);
                AddEngines(passatB8, [
                    EV("1.6 TDI 120 KM",  120, 88,  1598, 250, 116, "Euro 6d", 4.4m,  10.1m, 205, "FWD", "manual",    4, die),
                    EV("2.0 TDI 150 KM",  150, 110, 1968, 340, 122, "Euro 6d", 4.7m,   8.6m, 218, "FWD", "dsg",       4, die),
                    EV("2.0 TSI 220 KM",  220, 162, 1984, 350, 155, "Euro 6d", 6.7m,   6.9m, 245, "FWD", "dsg",       4, ben),
                ]);

                // Tiguan — Tiguan II
                int tiguan = GetOrCreateModel(vw, "Tiguan", "vw-tiguan");
                int tiguanII = GetOrCreateGeneration(tiguan, "Tiguan II", "vw-tiguan-ii", 2016, 2023);
                AddEngines(tiguanII, [
                    EV("1.5 TSI 150 KM",       150, 110, 1498, 250, 148, "Euro 6d", 6.4m,  8.5m, 214, "FWD", "dsg",  4, ben),
                    EV("2.0 TDI 150 KM",       150, 110, 1968, 340, 148, "Euro 6d", 5.6m,  9.1m, 207, "4WD", "dsg",  4, die),
                    EV("2.0 TSI 220 KM 4Motion",220,162, 1984, 350, 177, "Euro 6d", 7.5m,  6.5m, 228, "4WD", "dsg",  4, ben),
                ]);
            }
        }

        // ─── SKODA ────────────────────────────────────────────────────────────────
        {
            int skoda = B("Skoda");
            if (skoda > 0)
            {
                // Octavia IV
                int octavia = GetOrCreateModel(skoda, "Octavia", "skoda-octavia");
                int octaviaIV = GetOrCreateGeneration(octavia, "Octavia IV", "skoda-octavia-iv", 2020, null);
                AddEngines(octaviaIV, [
                    EV("1.0 TSI 110 KM",  110, 81,  999,  200, 118, "Euro 6d", 5.3m, 10.5m, 196, "FWD", "manual", 3, ben),
                    EV("1.5 TSI 150 KM",  150, 110, 1498, 250, 129, "Euro 6d", 5.7m,  8.0m, 227, "FWD", "dsg",    4, ben),
                    EV("2.0 TDI 150 KM",  150, 110, 1968, 360, 121, "Euro 6d", 4.6m,  8.2m, 220, "FWD", "dsg",    4, die),
                ]);

                // Kodiaq I
                int kodiaq = GetOrCreateModel(skoda, "Kodiaq", "skoda-kodiaq");
                int kodiaqI = GetOrCreateGeneration(kodiaq, "Kodiaq I", "skoda-kodiaq-i", 2016, 2023);
                AddEngines(kodiaqI, [
                    EV("1.5 TSI 150 KM",       150, 110, 1498, 250, 162, "Euro 6d", 7.0m,  9.4m, 201, "FWD", "dsg",  4, ben),
                    EV("2.0 TDI 150 KM 4x4",   150, 110, 1968, 340, 152, "Euro 6d", 5.8m,  9.5m, 201, "4WD", "dsg",  4, die),
                ]);
            }
        }

        // ─── TOYOTA ───────────────────────────────────────────────────────────────
        {
            int toyota = B("Toyota");
            if (toyota > 0)
            {
                // Corolla E210
                int corolla = GetOrCreateModel(toyota, "Corolla", "toyota-corolla");
                int corollaE210 = GetOrCreateGeneration(corolla, "Corolla E210", "toyota-corolla-e210", 2018, null);
                AddEngines(corollaE210, [
                    EV("1.8 Hybrid 122 KM",  122, 90,  1798, 142,  96, "Euro 6d", 4.1m, 11.0m, 180, "FWD", "cvt", 4, hyb),
                    EV("2.0 Hybrid 184 KM",  184, 135, 1987, 190, 103, "Euro 6d", 4.4m,  7.9m, 180, "FWD", "cvt", 4, hyb),
                ]);

                // RAV4 V
                int rav4 = GetOrCreateModel(toyota, "RAV4", "toyota-rav4");
                int rav4V = GetOrCreateGeneration(rav4, "RAV4 V", "toyota-rav4-v", 2018, null);
                AddEngines(rav4V, [
                    EV("2.5 Hybrid 222 KM",  222, 163, 2487, 221, 102, "Euro 6d", 4.5m,  8.4m, 180, "AWD", "cvt",    4, hyb),
                    EV("2.0 D-4D 150 KM",    150, 110, 1995, 400, 151, "Euro 6d", 5.7m,  9.9m, 184, "FWD", "manual", 4, die),
                ]);

                // Yaris IV
                int yaris = GetOrCreateModel(toyota, "Yaris", "toyota-yaris");
                int yarisIV = GetOrCreateGeneration(yaris, "Yaris IV", "toyota-yaris-iv", 2020, null);
                AddEngines(yarisIV, [
                    EV("1.5 Hybrid 116 KM",  116, 85, 1490, 141, 83, "Euro 6d", 3.6m, 9.3m, 175, "FWD", "cvt", 3, hyb),
                ]);
            }
        }

        // ─── BMW ──────────────────────────────────────────────────────────────────
        {
            int bmw = B("BMW");
            if (bmw > 0)
            {
                // Seria 3 G20
                int s3 = GetOrCreateModel(bmw, "Seria 3", "bmw-seria-3");
                int s3G20 = GetOrCreateGeneration(s3, "G20", "bmw-3-g20", 2018, null);
                AddEngines(s3G20, [
                    EV("318i 156 KM",  156, 115, 1499, 250, 135, "Euro 6d", 5.9m,  8.3m, 220, "RWD", "automatic", 4, ben),
                    EV("320i 184 KM",  184, 135, 1998, 300, 141, "Euro 6d", 6.1m,  7.1m, 240, "RWD", "automatic", 4, ben),
                    EV("330i 258 KM",  258, 190, 1998, 400, 155, "Euro 6d", 6.7m,  5.8m, 250, "RWD", "automatic", 4, ben),
                    EV("318d 150 KM",  150, 110, 1995, 360, 125, "Euro 6d", 4.9m,  8.4m, 218, "RWD", "automatic", 4, die),
                    EV("320d 190 KM",  190, 140, 1995, 400, 128, "Euro 6d", 4.9m,  6.8m, 240, "RWD", "automatic", 4, die),
                ]);

                // Seria 5 G30
                int s5 = GetOrCreateModel(bmw, "Seria 5", "bmw-seria-5");
                int s5G30 = GetOrCreateGeneration(s5, "G30", "bmw-5-g30", 2016, 2023);
                AddEngines(s5G30, [
                    EV("520i 184 KM",  184, 135, 1998, 300, 152, "Euro 6d", 6.6m,  7.5m, 240, "RWD", "automatic", 4, ben),
                    EV("530i 252 KM",  252, 185, 1998, 350, 162, "Euro 6d", 7.0m,  6.0m, 250, "RWD", "automatic", 4, ben),
                    EV("520d 190 KM",  190, 140, 1995, 400, 138, "Euro 6d", 5.3m,  7.5m, 240, "RWD", "automatic", 4, die),
                    EV("530d 286 KM",  286, 210, 2993, 650, 145, "Euro 6d", 5.6m,  5.7m, 250, "RWD", "automatic", 6, die),
                ]);

                // X5 G05
                int x5 = GetOrCreateModel(bmw, "X5", "bmw-x5");
                int x5G05 = GetOrCreateGeneration(x5, "G05", "bmw-x5-g05", 2018, null);
                AddEngines(x5G05, [
                    EV("xDrive30d 286 KM",  286, 210, 2993, 650, 188, "Euro 6d", 7.1m, 6.1m, 245, "AWD", "automatic", 6, die),
                    EV("xDrive40i 340 KM",  340, 250, 2998, 450, 219, "Euro 6d", 9.5m, 5.5m, 250, "AWD", "automatic", 6, ben),
                ]);
            }
        }

        // ─── MERCEDES-BENZ ────────────────────────────────────────────────────────
        {
            int mb = B("Mercedes-Benz");
            if (mb > 0)
            {
                // Klasa C W206
                int klasaC = GetOrCreateModel(mb, "Klasa C", "mb-klasa-c");
                int cW206 = GetOrCreateGeneration(klasaC, "W206", "mb-c-w206", 2021, null);
                AddEngines(cW206, [
                    EV("C 200 204 KM",       204, 150, 1499, 300,  155, "Euro 6d", 6.7m,  7.3m, 240, "RWD", "automatic", 4, ben),
                    EV("C 220d 200 KM",      200, 147, 1993, 440,  133, "Euro 6d", 5.1m,  7.1m, 240, "RWD", "automatic", 4, die),
                    EV("C 300 258 KM",       258, 190, 1999, 400,  175, "Euro 6d", 7.5m,  5.9m, 250, "RWD", "automatic", 4, ben),
                    EV("C 300e 313 KM PHEV", 313, 230, 1999, 550,   14, "Euro 6d", 0.6m,  5.9m, 250, "RWD", "automatic", 4, phev),
                ]);

                // Klasa E W213
                int klasaE = GetOrCreateModel(mb, "Klasa E", "mb-klasa-e");
                int eW213 = GetOrCreateGeneration(klasaE, "W213", "mb-e-w213", 2016, 2023);
                AddEngines(eW213, [
                    EV("E 200 197 KM",  197, 145, 1499, 320, 152, "Euro 6d", 6.5m, 7.9m, 240, "RWD", "automatic", 4, ben),
                    EV("E 220d 194 KM", 194, 143, 1950, 400, 133, "Euro 6d", 5.0m, 7.3m, 240, "RWD", "automatic", 4, die),
                    EV("E 300 258 KM",  258, 190, 1999, 370, 170, "Euro 6d", 7.3m, 6.1m, 250, "RWD", "automatic", 4, ben),
                ]);

                // GLC X254
                int glc = GetOrCreateModel(mb, "GLC", "mb-glc");
                int glcX254 = GetOrCreateGeneration(glc, "X254", "mb-glc-x254", 2022, null);
                AddEngines(glcX254, [
                    EV("GLC 200 204 KM",  204, 150, 1499, 320, 193, "Euro 6d", 8.3m, 7.7m, 220, "AWD", "automatic", 4, ben),
                    EV("GLC 220d 197 KM", 197, 145, 1993, 440, 163, "Euro 6d", 6.2m, 7.7m, 220, "AWD", "automatic", 4, die),
                ]);
            }
        }

        // ─── AUDI ─────────────────────────────────────────────────────────────────
        {
            int audi = B("Audi");
            if (audi > 0)
            {
                // A4 B9
                int a4 = GetOrCreateModel(audi, "A4", "audi-a4");
                int a4B9 = GetOrCreateGeneration(a4, "B9", "audi-a4-b9", 2015, 2023);
                AddEngines(a4B9, [
                    EV("35 TFSI 150 KM",  150, 110, 1498, 270, 134, "Euro 6d", 5.8m, 8.5m, 224, "FWD", "dsg",  4, ben),
                    EV("40 TFSI 204 KM",  204, 150, 1984, 340, 148, "Euro 6d", 6.4m, 7.1m, 240, "FWD", "dsg",  4, ben),
                    EV("35 TDI 163 KM",   163, 120, 1968, 380, 120, "Euro 6d", 4.6m, 8.3m, 224, "FWD", "dsg",  4, die),
                    EV("40 TDI 204 KM",   204, 150, 1968, 400, 127, "Euro 6d", 4.8m, 7.1m, 240, "AWD", "dsg",  4, die),
                ]);

                // Q5 FY
                int q5 = GetOrCreateModel(audi, "Q5", "audi-q5");
                int q5FY = GetOrCreateGeneration(q5, "FY", "audi-q5-fy", 2016, null);
                AddEngines(q5FY, [
                    EV("40 TFSI 204 KM",  204, 150, 1984, 320, 192, "Euro 6d", 8.3m, 7.4m, 225, "AWD", "dsg", 4, ben),
                    EV("40 TDI 204 KM",   204, 150, 1968, 400, 163, "Euro 6d", 6.2m, 7.2m, 224, "AWD", "dsg", 4, die),
                ]);
            }
        }

        // ─── FORD ─────────────────────────────────────────────────────────────────
        {
            int ford = B("Ford");
            if (ford > 0)
            {
                // Focus IV
                int focus = GetOrCreateModel(ford, "Focus", "ford-focus");
                int focusIV = GetOrCreateGeneration(focus, "Focus IV", "ford-focus-iv", 2018, null);
                AddEngines(focusIV, [
                    EV("1.0 EcoBoost 125 KM", 125, 92,  999,  210, 113, "Euro 6d", 4.9m, 10.5m, 202, "FWD", "manual",    3, ben),
                    EV("1.5 EcoBoost 182 KM", 182, 134, 1499, 290, 138, "Euro 6d", 6.0m,  7.7m, 220, "FWD", "automatic", 3, ben),
                    EV("1.5 EcoBlue 120 KM",  120, 88,  1499, 300, 113, "Euro 6d", 4.3m, 10.0m, 196, "FWD", "manual",    4, die),
                ]);

                // Kuga III
                int kuga = GetOrCreateModel(ford, "Kuga", "ford-kuga");
                int kugaIII = GetOrCreateGeneration(kuga, "Kuga III", "ford-kuga-iii", 2019, null);
                AddEngines(kugaIII, [
                    EV("1.5 EcoBoost 150 KM",    150, 110, 1499, 240,  151, "Euro 6d", 6.5m,  9.4m, 203, "FWD", "manual", 3, ben),
                    EV("2.5 Duratec PHEV 225 KM", 225, 165, 2488, 200,   26, "Euro 6d", 1.1m,  9.2m, 200, "FWD", "cvt",   4, phev),
                ]);
            }
        }

        // ─── OPEL ─────────────────────────────────────────────────────────────────
        {
            int opel = B("Opel");
            if (opel > 0)
            {
                // Astra L
                int astra = GetOrCreateModel(opel, "Astra", "opel-astra");
                int astraL = GetOrCreateGeneration(astra, "Astra L", "opel-astra-l", 2021, null);
                AddEngines(astraL, [
                    EV("1.2 Turbo 110 KM",  110, 81,  1199, 205, 127, "Euro 6d", 5.5m, 10.5m, 194, "FWD", "manual",    3, ben),
                    EV("1.2 Turbo 130 KM",  130, 96,  1199, 230, 131, "Euro 6d", 5.7m,  9.7m, 204, "FWD", "automatic", 3, ben),
                    EV("1.5 Diesel 130 KM", 130, 96,  1499, 300, 119, "Euro 6d", 4.6m,  9.7m, 200, "FWD", "manual",    4, die),
                ]);
            }
        }

        // ─── HYUNDAI ──────────────────────────────────────────────────────────────
        {
            int hyundai = B("Hyundai");
            if (hyundai > 0)
            {
                // Tucson IV
                int tucson = GetOrCreateModel(hyundai, "Tucson", "hyundai-tucson");
                int tucsonIV = GetOrCreateGeneration(tucson, "Tucson IV", "hyundai-tucson-iv", 2020, null);
                AddEngines(tucsonIV, [
                    EV("1.6 T-GDI 150 KM",     150, 110, 1591, 253, 152, "Euro 6d", 6.6m,  9.5m, 193, "FWD", "manual",    4, ben),
                    EV("1.6 CRDi 136 KM",       136, 100, 1598, 320, 140, "Euro 6d", 5.3m, 10.2m, 189, "FWD", "manual",    4, die),
                    EV("1.6 T-GDI HEV 230 KM",  230, 169, 1591, 265, 126, "Euro 6d", 5.4m,  8.3m, 193, "AWD", "automatic", 4, hyb),
                ]);

                // i30 III
                int i30 = GetOrCreateModel(hyundai, "i30", "hyundai-i30");
                int i30III = GetOrCreateGeneration(i30, "i30 III", "hyundai-i30-iii", 2017, null);
                AddEngines(i30III, [
                    EV("1.0 T-GDI 120 KM",  120, 88,  998,  172, 118, "Euro 6d", 5.1m, 10.5m, 196, "FWD", "manual", 3, ben),
                    EV("1.5 T-GDI 160 KM",  160, 118, 1482, 253, 133, "Euro 6d", 5.7m,  8.2m, 215, "FWD", "dsg",    4, ben),
                ]);
            }
        }

        // ─── KIA ──────────────────────────────────────────────────────────────────
        {
            int kia = B("Kia");
            if (kia > 0)
            {
                // Sportage V
                int sportage = GetOrCreateModel(kia, "Sportage", "kia-sportage");
                int sportageV = GetOrCreateGeneration(sportage, "Sportage V", "kia-sportage-v", 2021, null);
                AddEngines(sportageV, [
                    EV("1.6 T-GDI 150 KM",  150, 110, 1591, 253, 151, "Euro 6d", 6.5m,  9.3m, 195, "FWD", "manual",    4, ben),
                    EV("1.6 HEV 230 KM",     230, 169, 1591, 350, 130, "Euro 6d", 5.6m,  8.4m, 193, "AWD", "automatic", 4, hyb),
                    EV("1.6 CRDi 136 KM",    136, 100, 1598, 320, 141, "Euro 6d", 5.4m, 10.1m, 191, "FWD", "manual",    4, die),
                ]);

                // Ceed III
                int ceed = GetOrCreateModel(kia, "Ceed", "kia-ceed");
                int ceedIII = GetOrCreateGeneration(ceed, "Ceed III", "kia-ceed-iii", 2018, null);
                AddEngines(ceedIII, [
                    EV("1.0 T-GDI 120 KM",  120, 88,  998,  172, 117, "Euro 6d", 5.1m, 10.7m, 195, "FWD", "manual", 3, ben),
                    EV("1.4 T-GDI 140 KM",  140, 103, 1353, 242, 129, "Euro 6d", 5.6m,  9.3m, 205, "FWD", "dsg",    4, ben),
                ]);
            }
        }

        // ─── RENAULT ──────────────────────────────────────────────────────────────
        {
            int renault = B("Renault");
            if (renault > 0)
            {
                // Clio V
                int clio = GetOrCreateModel(renault, "Clio", "renault-clio");
                int clioV = GetOrCreateGeneration(clio, "Clio V", "renault-clio-v", 2019, null);
                AddEngines(clioV, [
                    EV("1.0 TCe 90 KM",          90,  66, 999,  160,  117, "Euro 6d", 5.1m, 13.0m, 176, "FWD", "manual",    3, ben),
                    EV("E-TECH Hybrid 145 KM",   145, 107, 1598, 148,   96, "Euro 6d", 4.1m,  9.9m, 180, "FWD", "automatic", 4, hyb),
                ]);

                // Kadjar I
                int kadjar = GetOrCreateModel(renault, "Kadjar", "renault-kadjar");
                int kadjarI = GetOrCreateGeneration(kadjar, "Kadjar I", "renault-kadjar-i", 2015, 2022);
                AddEngines(kadjarI, [
                    EV("1.3 TCe 140 KM",  140, 103, 1332, 260, 148, "Euro 6d", 6.4m, 9.5m, 200, "FWD", "dsg",    4, ben),
                    EV("1.7 dCi 150 KM",  150, 110, 1749, 340, 133, "Euro 6d", 5.1m, 9.5m, 198, "FWD", "dsg",    4, die),
                ]);
            }
        }

        // ─── PEUGEOT ──────────────────────────────────────────────────────────────
        {
            int peugeot = B("Peugeot");
            if (peugeot > 0)
            {
                // 308 III
                int p308 = GetOrCreateModel(peugeot, "308", "peugeot-308");
                int p308III = GetOrCreateGeneration(p308, "308 III", "peugeot-308-iii", 2021, null);
                AddEngines(p308III, [
                    EV("1.2 PureTech 130 KM",   130,  96, 1199, 230, 121, "Euro 6d", 5.3m,  9.4m, 204, "FWD", "automatic", 3, ben),
                    EV("1.5 BlueHDi 130 KM",    130,  96, 1499, 300, 120, "Euro 6d", 4.6m, 10.3m, 200, "FWD", "manual",    4, die),
                    EV("Hybrid 225 KM PHEV",     225, 165, 1199, 360,  28, "Euro 6d", 1.2m,  7.5m, 225, "FWD", "automatic", 3, phev),
                ]);

                // 2008 II
                int p2008 = GetOrCreateModel(peugeot, "2008", "peugeot-2008");
                int p2008II = GetOrCreateGeneration(p2008, "2008 II", "peugeot-2008-ii", 2019, null);
                AddEngines(p2008II, [
                    EV("1.2 PureTech 100 KM",  100, 74, 1199, 205, 129, "Euro 6d", 5.6m, 11.3m, 182, "FWD", "manual",    3, ben),
                    EV("1.2 PureTech 130 KM",  130, 96, 1199, 230, 131, "Euro 6d", 5.7m,  9.4m, 197, "FWD", "automatic", 3, ben),
                ]);
            }
        }

        // ─── VOLVO ────────────────────────────────────────────────────────────────
        {
            int volvo = B("Volvo");
            if (volvo > 0)
            {
                // XC60 II
                int xc60 = GetOrCreateModel(volvo, "XC60", "volvo-xc60");
                int xc60II = GetOrCreateGeneration(xc60, "XC60 II", "volvo-xc60-ii", 2017, null);
                AddEngines(xc60II, [
                    EV("B4 Benzyna 197 KM",  197, 145, 1969, 300, 183, "Euro 6d", 7.9m, 8.4m, 210, "AWD", "automatic", 4, ben),
                    EV("B5 Benzyna 250 KM",  250, 184, 1969, 350, 191, "Euro 6d", 8.2m, 6.8m, 230, "AWD", "automatic", 4, ben),
                    EV("D4 Diesel 190 KM",   190, 140, 1969, 400, 153, "Euro 6d", 5.8m, 8.0m, 210, "AWD", "automatic", 4, die),
                    EV("T8 PHEV 390 KM",     390, 287, 1969, 640,  27, "Euro 6d", 1.2m, 5.3m, 230, "AWD", "automatic", 4, phev),
                ]);
            }
        }

        // ─── MAZDA ────────────────────────────────────────────────────────────────
        {
            int mazda = B("Mazda");
            if (mazda > 0)
            {
                // CX-5 II
                int cx5 = GetOrCreateModel(mazda, "CX-5", "mazda-cx5");
                int cx5II = GetOrCreateGeneration(cx5, "CX-5 II", "mazda-cx5-ii", 2017, null);
                AddEngines(cx5II, [
                    EV("2.0 SKYACTIV-G 165 KM",  165, 121, 1998, 213, 154, "Euro 6d", 6.7m,  9.8m, 195, "FWD", "manual",    4, ben),
                    EV("2.5 SKYACTIV-G 194 KM",  194, 143, 2488, 258, 188, "Euro 6d", 8.1m,  8.7m, 200, "AWD", "automatic", 4, ben),
                    EV("2.2 SKYACTIV-D 150 KM",  150, 110, 2191, 380, 132, "Euro 6d", 5.0m,  9.4m, 195, "FWD", "manual",    4, die),
                    EV("2.2 SKYACTIV-D 184 KM",  184, 135, 2191, 450, 139, "Euro 6d", 5.3m,  8.2m, 215, "AWD", "automatic", 4, die),
                ]);
            }
        }

        // ─── SEAT ─────────────────────────────────────────────────────────────────
        {
            int seat = B("Seat");
            if (seat > 0)
            {
                // Leon IV
                int leon = GetOrCreateModel(seat, "Leon", "seat-leon");
                int leonIV = GetOrCreateGeneration(leon, "Leon IV", "seat-leon-iv", 2020, null);
                AddEngines(leonIV, [
                    EV("1.0 TSI 110 KM",        110, 81,  999,  200, 117, "Euro 6d", 5.1m, 10.2m, 193, "FWD", "manual", 3, ben),
                    EV("1.5 TSI 150 KM",        150, 110, 1498, 250, 125, "Euro 6d", 5.4m,  8.4m, 224, "FWD", "dsg",    4, ben),
                    EV("2.0 TDI 150 KM",        150, 110, 1968, 360, 121, "Euro 6d", 4.6m,  8.4m, 220, "FWD", "dsg",    4, die),
                    EV("1.4 e-HYBRID 204 KM",   204, 150, 1395, 350,  26, "Euro 6d", 1.1m,  7.5m, 210, "FWD", "dsg",    4, phev),
                ]);
            }
        }

        // ─── NISSAN ───────────────────────────────────────────────────────────────
        {
            int nissan = B("Nissan");
            if (nissan > 0)
            {
                // Qashqai III
                int qashqai = GetOrCreateModel(nissan, "Qashqai", "nissan-qashqai");
                int qashqaiIII = GetOrCreateGeneration(qashqai, "Qashqai III", "nissan-qashqai-iii", 2021, null);
                AddEngines(qashqaiIII, [
                    EV("1.3 DIG-T 140 KM",  140, 103, 1332, 240, 138, "Euro 6d", 6.0m, 10.2m, 198, "FWD", "manual",    4, ben),
                    EV("e-POWER 190 KM",     190, 140, 1497, 330, 136, "Euro 6d", 5.8m,  7.9m, 170, "FWD", "automatic", 3, hyb),
                ]);

                // Leaf II
                int leaf = GetOrCreateModel(nissan, "Leaf", "nissan-leaf");
                int leafII = GetOrCreateGeneration(leaf, "Leaf II", "nissan-leaf-ii", 2017, null);
                AddEngines(leafII, [
                    EV("40 kWh 150 KM",   150, 110, 0, 320, 0, "Euro 6d", 0.0m, 7.9m, 150, "FWD", "automatic", 0, ev),
                    EV("62 kWh 218 KM",   218, 160, 0, 340, 0, "Euro 6d", 0.0m, 6.5m, 158, "FWD", "automatic", 0, ev),
                ]);
            }
        }

        // ─── DACIA ────────────────────────────────────────────────────────────────
        {
            int dacia = B("Dacia");
            if (dacia > 0)
            {
                // Sandero III
                int sandero = GetOrCreateModel(dacia, "Sandero", "dacia-sandero");
                int sanderoIII = GetOrCreateGeneration(sandero, "Sandero III", "dacia-sandero-iii", 2020, null);
                AddEngines(sanderoIII, [
                    EV("1.0 SCe 65 KM",          65, 48, 999, 91,  122, "Euro 6d", 5.4m, 17.0m, 160, "FWD", "manual", 3, ben),
                    EV("1.0 TCe 90 KM",           90, 66, 999, 160, 117, "Euro 6d", 5.1m, 12.5m, 176, "FWD", "manual", 3, ben),
                    EV("1.0 TCe ECO-G 100 KM LPG",100,74, 999, 160, 122, "Euro 6d", 5.3m, 12.0m, 178, "FWD", "manual", 3, lpg),
                ]);

                // Duster II
                int duster = GetOrCreateModel(dacia, "Duster", "dacia-duster");
                int dusterII = GetOrCreateGeneration(duster, "Duster II", "dacia-duster-ii", 2017, null);
                AddEngines(dusterII, [
                    EV("1.5 dCi 115 KM",   115, 85,  1461, 260, 138, "Euro 6d", 5.3m, 12.0m, 178, "FWD", "manual", 4, die),
                    EV("1.3 TCe 150 KM",   150, 110, 1332, 250, 163, "Euro 6d", 7.1m, 10.0m, 183, "4WD", "dsg",    4, ben),
                ]);
            }
        }

        // ─── HONDA ────────────────────────────────────────────────────────────────
        {
            int honda = B("Honda");
            if (honda > 0)
            {
                // CR-V V
                int crv = GetOrCreateModel(honda, "CR-V", "honda-crv");
                int crvV = GetOrCreateGeneration(crv, "CR-V V", "honda-crv-v", 2018, null);
                AddEngines(crvV, [
                    EV("1.5 VTEC Turbo 173 KM",   173, 127, 1498, 243, 164, "Euro 6d", 7.0m, 9.4m, 196, "AWD", "cvt", 4, ben),
                    EV("2.0 i-MMD 184 KM Hybrid",  184, 135, 1993, 315, 132, "Euro 6d", 5.7m, 8.9m, 180, "AWD", "cvt", 4, hyb),
                ]);
            }
        }

        logger.LogInformation("[VehicleDataSeeder] Completed seeding enriched engine versions with factory specs.");
    }

    /// <summary>Creates an EngineVersion with full spec fields.</summary>
    private static EngineVersion EV(
        string name, int hp, int kw, int? disp,
        int torque, int co2, string euro,
        decimal consumption, decimal accel, int topSpeed,
        string drive, string gearbox, int cylinders, int fuelTypeId)
    {
        return new EngineVersion
        {
            EngineName       = name,
            PowerHP          = hp,
            PowerKW          = kw,
            Displacement     = disp == 0 ? null : disp,
            TorqueNm         = torque,
            Co2EmissionGkm   = co2 == 0 ? null : co2,
            EuroNorm         = euro,
            AvgConsumptionL  = consumption == 0 ? null : consumption,
            Acceleration0100 = accel,
            TopSpeedKmh      = topSpeed,
            DriveType        = drive,
            GearboxType      = gearbox,
            Cylinders        = cylinders == 0 ? null : cylinders,
            FuelTypeId       = fuelTypeId,
        };
    }
}
