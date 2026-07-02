using CarsWebsite;
using cars_website_api.CarsWebsite.Domain.Entities;

namespace cars_website_api.CarsWebsite.Data;

public static class ModelSeeder
{
    public static void SeedModelsGenerationsEngines(AppDbContext db, ILogger logger)
    {
        // GroupBy+First instead of ToDictionary: a duplicate Brand/FuelType name must not
        // crash the whole seeder chain (this runs first, before every other seeder).
        var brands = db.Brands.AsEnumerable()
            .GroupBy(b => b.Name).ToDictionary(g => g.Key, g => g.OrderBy(b => b.Id).First().Id);
        var fuels  = db.FuelTypes.AsEnumerable()
            .GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.OrderBy(f => f.Id).First().Id);
        if (!brands.Any() || !fuels.Any()) return;

        // Brands that already have at least one model — skip seeding those
        var seededBrandIds = db.Models.Select(m => m.BrandId).Distinct().ToHashSet();

        int B(string n) => brands.TryGetValue(n, out var id) ? id : 0;
        int F(string n) => fuels.TryGetValue(n, out var id) ? id : 0;
        bool NeedsSeeding(string n) { var id = B(n); return id > 0 && !seededBrandIds.Contains(id); }

        int ben  = F("Benzyna"), die = F("Diesel"), hyb = F("Hybryda"),
            phev = F("Hybryda plug-in"), ev = F("Elektryczny"), mild = F("Hybryda mild");

        // Helper: create EngineVersion
        static EngineVersion E(string name, int hp, int kw, int? disp, int fuelId, decimal? fuelCity = null, decimal? fuelHwy = null, decimal? fuelMix = null) =>
            new() { EngineName = name, PowerHP = hp, PowerKW = kw, Displacement = disp, FuelTypeId = fuelId,
                    FuelConsumptionCity = fuelCity, FuelConsumptionHighway = fuelHwy, FuelConsumptionCombined = fuelMix };

        // Helper: create Generation with engines
        static Generation G(string name, string slug, int yFrom, int? yTo, params EngineVersion[] engines) =>
            new() { Name = name, Slug = slug, YearFrom = yFrom, YearTo = yTo, EngineVersions = engines.ToList() };

        var models = new List<Model>();

        // ─── VOLKSWAGEN ───────────────────────────────────────────────────────────
        if (NeedsSeeding("Volkswagen")) models.AddRange([
            new Model { BrandId = B("Volkswagen"), Name = "Golf", Slug = "vw-golf", Generations = [
                G("Mk7 (2012–2019)", "vw-golf-mk7", 2012, 2019,
                    E("1.0 TSI 115 KM", 115, 85, 999, ben, 8.0m, 5.5m, 6.5m), E("1.4 TSI 125 KM", 125, 92, 1395, ben, 7.5m, 5.0m, 6.0m),
                    E("1.5 TSI 130 KM", 130, 96, 1498, ben, 8.5m, 5.5m, 6.5m), E("2.0 GTI TSI 220 KM", 220, 162, 1984, ben, 10.5m, 7.0m, 8.5m),
                    E("1.6 TDI 115 KM", 115, 85, 1598, die, 6.0m, 4.2m, 5.0m), E("2.0 TDI 150 KM", 150, 110, 1968, die, 6.0m, 4.2m, 5.0m)),
                G("Mk8 (2019–)", "vw-golf-mk8", 2019, null,
                    E("1.0 eTSI 110 KM", 110, 81, 999, mild, 8.0m, 5.5m, 6.5m), E("1.5 eTSI 150 KM", 150, 110, 1498, mild, 8.5m, 5.5m, 6.5m),
                    E("2.0 GTI TSI 245 KM", 245, 180, 1984, ben, 10.5m, 7.0m, 8.5m),
                    E("2.0 TDI 115 KM", 115, 85, 1968, die, 6.0m, 4.2m, 5.0m), E("2.0 TDI 150 KM", 150, 110, 1968, die, 6.0m, 4.2m, 5.0m),
                    E("GTE eHybrid 204 KM", 204, 150, 1395, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("Volkswagen"), Name = "Passat", Slug = "vw-passat", Generations = [
                G("B8 (2014–2023)", "vw-passat-b8", 2014, 2023,
                    E("1.5 TSI 150 KM", 150, 110, 1498, ben, 8.5m, 5.5m, 6.5m), E("2.0 TSI 220 KM", 220, 162, 1984, ben, 10.5m, 7.0m, 8.5m),
                    E("1.6 TDI 120 KM", 120, 88, 1598, die, 6.0m, 4.2m, 5.0m), E("2.0 TDI 150 KM", 150, 110, 1968, die, 6.0m, 4.2m, 5.0m),
                    E("2.0 TDI 190 KM", 190, 140, 1968, die, 6.5m, 4.5m, 5.5m), E("GTE 218 KM", 218, 160, 1395, phev, 2.0m, 5.0m, 3.0m)),
                G("B9 (2023–)", "vw-passat-b9", 2023, null,
                    E("1.5 eTSI 150 KM", 150, 110, 1498, mild, 8.5m, 5.5m, 6.5m), E("2.0 TSI 265 KM", 265, 195, 1984, ben, 12.0m, 8.0m, 10.0m),
                    E("2.0 TDI 150 KM", 150, 110, 1968, die, 6.0m, 4.2m, 5.0m), E("2.0 TDI 193 KM", 193, 142, 1968, die, 6.5m, 4.5m, 5.5m),
                    E("GTE 272 KM", 272, 200, 1498, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("Volkswagen"), Name = "Tiguan", Slug = "vw-tiguan", Generations = [
                G("Mk1 (2007–2016)", "vw-tiguan-mk1", 2007, 2016,
                    E("1.4 TSI 125 KM", 125, 92, 1390, ben, 7.5m, 5.0m, 6.0m), E("2.0 TSI 200 KM", 200, 147, 1984, ben, 9.5m, 6.0m, 7.5m),
                    E("2.0 TDI 110 KM", 110, 81, 1968, die, 6.0m, 4.2m, 5.0m), E("2.0 TDI 140 KM", 140, 103, 1968, die, 6.0m, 4.2m, 5.0m)),
                G("Mk2 (2016–)", "vw-tiguan-mk2", 2016, null,
                    E("1.5 TSI 130 KM", 130, 96, 1498, ben, 8.5m, 5.5m, 6.5m), E("2.0 TSI 190 KM", 190, 140, 1984, ben, 9.5m, 6.0m, 7.5m),
                    E("2.0 TSI 320 KM R", 320, 235, 1984, ben, 12.0m, 8.0m, 10.0m),
                    E("2.0 TDI 150 KM", 150, 110, 1968, die, 6.0m, 4.2m, 5.0m), E("2.0 TDI 200 KM", 200, 147, 1968, die, 6.5m, 4.5m, 5.5m),
                    E("eHybrid 245 KM", 245, 180, 1395, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("Volkswagen"), Name = "Polo", Slug = "vw-polo", Generations = [
                G("AW (2017–)", "vw-polo-aw", 2017, null,
                    E("1.0 MPI 65 KM", 65, 48, 999, ben, 8.0m, 5.5m, 6.5m), E("1.0 TSI 95 KM", 95, 70, 999, ben, 8.0m, 5.5m, 6.5m),
                    E("1.0 TSI 110 KM", 110, 81, 999, ben, 8.0m, 5.5m, 6.5m), E("2.0 TSI GTI 207 KM", 207, 152, 1984, ben, 10.5m, 7.0m, 8.5m),
                    E("1.6 TDI 80 KM", 80, 59, 1598, die, 5.5m, 4.0m, 4.5m), E("1.6 TDI 95 KM", 95, 70, 1598, die, 5.5m, 4.0m, 4.5m)) ]},
            new Model { BrandId = B("Volkswagen"), Name = "T-Roc", Slug = "vw-t-roc", Generations = [
                G("Mk1 (2017–)", "vw-t-roc-mk1", 2017, null,
                    E("1.0 TSI 115 KM", 115, 85, 999, ben, 8.0m, 5.5m, 6.5m), E("1.5 TSI 150 KM", 150, 110, 1498, ben, 8.5m, 5.5m, 6.5m),
                    E("2.0 TSI 300 KM R", 300, 221, 1984, ben, 12.0m, 8.0m, 10.0m),
                    E("1.6 TDI 115 KM", 115, 85, 1598, die, 5.5m, 4.0m, 4.5m), E("2.0 TDI 150 KM", 150, 110, 1968, die, 6.0m, 4.2m, 5.0m)) ]},
        ]);

        // ─── SKODA ────────────────────────────────────────────────────────────────
        if (NeedsSeeding("Skoda")) models.AddRange([
            new Model { BrandId = B("Skoda"), Name = "Octavia", Slug = "skoda-octavia", Generations = [
                G("III (2012–2020)", "skoda-octavia-iii", 2012, 2020,
                    E("1.0 TSI 115 KM", 115, 85, 999, ben, 8.0m, 5.5m, 6.5m), E("1.4 TSI 150 KM", 150, 110, 1395, ben, 7.5m, 5.0m, 6.0m),
                    E("2.0 TSI RS 230 KM", 230, 169, 1984, ben, 10.5m, 7.0m, 8.5m),
                    E("1.6 TDI 105 KM", 105, 77, 1598, die, 5.5m, 4.0m, 4.5m), E("2.0 TDI 150 KM", 150, 110, 1968, die, 6.0m, 4.2m, 5.0m),
                    E("2.0 TDI 184 KM", 184, 135, 1968, die, 6.5m, 4.5m, 5.5m)),
                G("IV (2020–)", "skoda-octavia-iv", 2020, null,
                    E("1.0 TSI 110 KM", 110, 81, 999, ben, 8.0m, 5.5m, 6.5m), E("1.5 TSI 150 KM", 150, 110, 1498, mild, 8.5m, 5.5m, 6.5m),
                    E("2.0 TSI RS 245 KM", 245, 180, 1984, ben, 10.5m, 7.0m, 8.5m),
                    E("2.0 TDI 115 KM", 115, 85, 1968, die, 6.0m, 4.2m, 5.0m), E("2.0 TDI 150 KM", 150, 110, 1968, die, 6.0m, 4.2m, 5.0m),
                    E("iV PHEV 245 KM", 245, 180, 1395, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("Skoda"), Name = "Fabia", Slug = "skoda-fabia", Generations = [
                G("III (2014–2021)", "skoda-fabia-iii", 2014, 2021,
                    E("1.0 MPI 60 KM", 60, 44, 999, ben, 8.0m, 5.5m, 6.5m), E("1.0 TSI 95 KM", 95, 70, 999, ben, 8.0m, 5.5m, 6.5m),
                    E("1.4 TSI 150 KM", 150, 110, 1395, ben, 7.5m, 5.0m, 6.0m), E("1.4 TDI 90 KM", 90, 66, 1422, die, 5.5m, 4.0m, 4.5m)),
                G("IV (2021–)", "skoda-fabia-iv", 2021, null,
                    E("1.0 MPI 65 KM", 65, 48, 999, ben, 8.0m, 5.5m, 6.5m), E("1.0 TSI 95 KM", 95, 70, 999, ben, 8.0m, 5.5m, 6.5m),
                    E("1.5 TSI 150 KM", 150, 110, 1498, ben, 8.5m, 5.5m, 6.5m)) ]},
            new Model { BrandId = B("Skoda"), Name = "Kodiaq", Slug = "skoda-kodiaq", Generations = [
                G("I (2016–2023)", "skoda-kodiaq-i", 2016, 2023,
                    E("1.5 TSI 150 KM", 150, 110, 1498, ben, 8.5m, 5.5m, 6.5m), E("2.0 TSI 180 KM", 180, 132, 1984, ben, 9.5m, 6.0m, 7.5m),
                    E("2.0 TSI RS 245 KM", 245, 180, 1984, ben, 10.5m, 7.0m, 8.5m),
                    E("2.0 TDI 150 KM", 150, 110, 1968, die, 6.0m, 4.2m, 5.0m), E("2.0 TDI 190 KM", 190, 140, 1968, die, 6.5m, 4.5m, 5.5m)),
                G("II (2023–)", "skoda-kodiaq-ii", 2023, null,
                    E("1.5 TSI 150 KM", 150, 110, 1498, mild, 8.5m, 5.5m, 6.5m), E("2.0 TSI 204 KM", 204, 150, 1984, ben, 9.5m, 6.0m, 7.5m),
                    E("2.0 TDI 150 KM", 150, 110, 1968, die, 6.0m, 4.2m, 5.0m), E("iV PHEV 204 KM", 204, 150, 1498, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("Skoda"), Name = "Superb", Slug = "skoda-superb", Generations = [
                G("III (2015–2023)", "skoda-superb-iii", 2015, 2023,
                    E("1.5 TSI 150 KM", 150, 110, 1498, ben, 8.5m, 5.5m, 6.5m), E("2.0 TSI 190 KM", 190, 140, 1984, ben, 9.5m, 6.0m, 7.5m),
                    E("2.0 TDI 150 KM", 150, 110, 1968, die, 6.0m, 4.2m, 5.0m), E("2.0 TDI 190 KM", 190, 140, 1968, die, 6.5m, 4.5m, 5.5m),
                    E("iV PHEV 218 KM", 218, 160, 1395, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("Skoda"), Name = "Kamiq", Slug = "skoda-kamiq", Generations = [
                G("I (2019–)", "skoda-kamiq-i", 2019, null,
                    E("1.0 TSI 95 KM", 95, 70, 999, ben, 8.0m, 5.5m, 6.5m), E("1.0 TSI 115 KM", 115, 85, 999, ben, 8.0m, 5.5m, 6.5m),
                    E("1.5 TSI 150 KM", 150, 110, 1498, ben, 8.5m, 5.5m, 6.5m)) ]},
        ]);

        // ─── AUDI ─────────────────────────────────────────────────────────────────
        if (NeedsSeeding("Audi")) models.AddRange([
            new Model { BrandId = B("Audi"), Name = "A3", Slug = "audi-a3", Generations = [
                G("8V (2012–2020)", "audi-a3-8v", 2012, 2020,
                    E("1.0 TFSI 116 KM", 116, 85, 999, ben, 8.0m, 5.5m, 6.5m), E("1.4 TFSI 150 KM", 150, 110, 1395, ben, 7.5m, 5.0m, 6.0m),
                    E("2.0 TFSI 190 KM", 190, 140, 1984, ben, 9.5m, 6.0m, 7.5m), E("2.0 TFSI S3 310 KM", 310, 228, 1984, ben, 12.0m, 8.0m, 10.0m),
                    E("1.6 TDI 116 KM", 116, 85, 1598, die, 5.5m, 4.0m, 4.5m), E("2.0 TDI 150 KM", 150, 110, 1968, die, 6.0m, 4.2m, 5.0m)),
                G("8Y (2020–)", "audi-a3-8y", 2020, null,
                    E("30 TFSI 110 KM", 110, 81, 999, mild, 8.0m, 5.5m, 6.5m), E("35 TFSI 150 KM", 150, 110, 1498, mild, 8.5m, 5.5m, 6.5m),
                    E("40 TFSI S3 310 KM", 310, 228, 1984, ben, 12.0m, 8.0m, 10.0m),
                    E("30 TDI 116 KM", 116, 85, 1968, die, 6.0m, 4.2m, 5.0m), E("35 TDI 150 KM", 150, 110, 1968, die, 6.0m, 4.2m, 5.0m),
                    E("45 TFSIe PHEV 204 KM", 204, 150, 1395, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("Audi"), Name = "A4", Slug = "audi-a4", Generations = [
                G("B8 (2007–2015)", "audi-a4-b8", 2007, 2015,
                    E("1.8 TFSI 160 KM", 160, 118, 1798, ben, 9.5m, 6.0m, 7.5m), E("2.0 TFSI 211 KM", 211, 155, 1984, ben, 9.5m, 6.0m, 7.5m),
                    E("3.0 TFSI 272 KM", 272, 200, 2995, ben, 14.0m, 9.0m, 11.5m),
                    E("2.0 TDI 143 KM", 143, 105, 1968, die, 6.0m, 4.2m, 5.0m), E("2.0 TDI 177 KM", 177, 130, 1968, die, 6.5m, 4.5m, 5.5m),
                    E("3.0 TDI 245 KM", 245, 180, 2967, die, 8.5m, 6.0m, 7.0m)),
                G("B9 (2015–)", "audi-a4-b9", 2015, null,
                    E("35 TFSI 150 KM", 150, 110, 1498, ben, 8.5m, 5.5m, 6.5m), E("40 TFSI 204 KM", 204, 150, 1984, ben, 9.5m, 6.0m, 7.5m),
                    E("45 TFSI 265 KM", 265, 195, 1984, ben, 12.0m, 8.0m, 10.0m), E("S4 TFSI 341 KM", 341, 251, 2995, ben, 14.0m, 9.0m, 11.5m),
                    E("30 TDI 136 KM", 136, 100, 1968, die, 6.0m, 4.2m, 5.0m), E("35 TDI 163 KM", 163, 120, 1968, die, 6.0m, 4.2m, 5.0m),
                    E("40 TDI 204 KM", 204, 150, 1968, die, 6.5m, 4.5m, 5.5m)) ]},
            new Model { BrandId = B("Audi"), Name = "A6", Slug = "audi-a6", Generations = [
                G("C7 (2011–2018)", "audi-a6-c7", 2011, 2018,
                    E("2.0 TFSI 252 KM", 252, 185, 1984, ben, 10.5m, 7.0m, 8.5m), E("3.0 TFSI 333 KM", 333, 245, 2995, ben, 14.0m, 9.0m, 11.5m),
                    E("S6 4.0 TFSI 420 KM", 420, 309, 3993, ben, 18.0m, 11.0m, 14.0m),
                    E("2.0 TDI 190 KM", 190, 140, 1968, die, 6.5m, 4.5m, 5.5m), E("3.0 TDI 218 KM", 218, 160, 2967, die, 8.5m, 6.0m, 7.0m),
                    E("3.0 TDI 272 KM", 272, 200, 2967, die, 8.5m, 6.0m, 7.0m)),
                G("C8 (2018–)", "audi-a6-c8", 2018, null,
                    E("40 TFSI 204 KM", 204, 150, 1984, mild, 9.5m, 6.0m, 7.5m), E("45 TFSI 265 KM", 265, 195, 1984, mild, 12.0m, 8.0m, 10.0m),
                    E("55 TFSI 340 KM", 340, 250, 2995, mild, 14.0m, 9.0m, 11.5m),
                    E("40 TDI 204 KM", 204, 150, 1968, mild, 6.5m, 4.5m, 5.5m), E("45 TDI 231 KM", 231, 170, 2967, mild, 8.5m, 6.0m, 7.0m),
                    E("55 TFSIe PHEV 367 KM", 367, 270, 2995, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("Audi"), Name = "Q5", Slug = "audi-q5", Generations = [
                G("8R (2008–2016)", "audi-q5-8r", 2008, 2016,
                    E("2.0 TFSI 225 KM", 225, 165, 1984, ben, 10.5m, 7.0m, 8.5m), E("3.0 TFSI 272 KM", 272, 200, 2995, ben, 14.0m, 9.0m, 11.5m),
                    E("2.0 TDI 150 KM", 150, 110, 1968, die, 6.0m, 4.2m, 5.0m), E("2.0 TDI 177 KM", 177, 130, 1968, die, 6.5m, 4.5m, 5.5m),
                    E("3.0 TDI 245 KM", 245, 180, 2967, die, 8.5m, 6.0m, 7.0m)),
                G("FY (2016–)", "audi-q5-fy", 2016, null,
                    E("40 TFSI 204 KM", 204, 150, 1984, mild, 9.5m, 6.0m, 7.5m), E("45 TFSI 265 KM", 265, 195, 1984, mild, 12.0m, 8.0m, 10.0m),
                    E("SQ5 TFSI 341 KM", 341, 251, 2995, ben, 14.0m, 9.0m, 11.5m),
                    E("35 TDI 163 KM", 163, 120, 1968, die, 6.0m, 4.2m, 5.0m), E("40 TDI 204 KM", 204, 150, 1968, die, 6.5m, 4.5m, 5.5m),
                    E("55 TFSIe PHEV 367 KM", 367, 270, 1984, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("Audi"), Name = "Q3", Slug = "audi-q3", Generations = [
                G("8U (2011–2018)", "audi-q3-8u", 2011, 2018,
                    E("1.4 TFSI 150 KM", 150, 110, 1395, ben, 7.5m, 5.0m, 6.0m), E("2.0 TFSI 211 KM", 211, 155, 1984, ben, 9.5m, 6.0m, 7.5m),
                    E("1.4 TDI 90 KM", 90, 66, 1422, die, 5.5m, 4.0m, 4.5m), E("2.0 TDI 150 KM", 150, 110, 1968, die, 6.0m, 4.2m, 5.0m)),
                G("F3 (2018–)", "audi-q3-f3", 2018, null,
                    E("35 TFSI 150 KM", 150, 110, 1498, ben, 8.5m, 5.5m, 6.5m), E("40 TFSI 190 KM", 190, 140, 1984, ben, 9.5m, 6.0m, 7.5m),
                    E("45 TFSI RS Q3 400 KM", 400, 294, 2480, ben, 14.0m, 9.0m, 11.5m),
                    E("35 TDI 150 KM", 150, 110, 1968, die, 6.0m, 4.2m, 5.0m)) ]},
        ]);

        // ─── BMW ──────────────────────────────────────────────────────────────────
        if (NeedsSeeding("BMW")) models.AddRange([
            new Model { BrandId = B("BMW"), Name = "Seria 3", Slug = "bmw-seria-3", Generations = [
                G("F30 (2011–2018)", "bmw-3-f30", 2011, 2018,
                    E("316i 136 KM", 136, 100, 1598, ben, 9.5m, 6.0m, 7.5m), E("318i 136 KM", 136, 100, 1499, ben, 9.5m, 6.0m, 7.5m),
                    E("320i 184 KM", 184, 135, 1998, ben, 9.5m, 6.0m, 7.5m), E("328i 245 KM", 245, 180, 1997, ben, 10.5m, 7.0m, 8.5m),
                    E("335i 306 KM", 306, 225, 2979, ben, 14.0m, 9.0m, 11.5m), E("M3 431 KM", 431, 317, 2979, ben, 18.0m, 11.0m, 14.0m),
                    E("316d 116 KM", 116, 85, 1995, die, 6.0m, 4.2m, 5.0m), E("318d 143 KM", 143, 105, 1995, die, 6.0m, 4.2m, 5.0m),
                    E("320d 190 KM", 190, 140, 1995, die, 6.5m, 4.5m, 5.5m), E("330d 258 KM", 258, 190, 2993, die, 8.5m, 6.0m, 7.0m)),
                G("G20 (2018–)", "bmw-3-g20", 2018, null,
                    E("318i 156 KM", 156, 115, 1499, mild, 9.5m, 6.0m, 7.5m), E("320i 184 KM", 184, 135, 1998, mild, 9.5m, 6.0m, 7.5m),
                    E("330i 258 KM", 258, 190, 1998, mild, 10.5m, 7.0m, 8.5m), E("M340i 374 KM", 374, 275, 2998, mild, 14.0m, 9.0m, 11.5m),
                    E("M3 Competition 510 KM", 510, 375, 2993, ben, 18.0m, 11.0m, 14.0m),
                    E("318d 150 KM", 150, 110, 1995, mild, 6.0m, 4.2m, 5.0m), E("320d 190 KM", 190, 140, 1995, mild, 6.5m, 4.5m, 5.5m),
                    E("330d 286 KM", 286, 210, 2993, mild, 8.5m, 6.0m, 7.0m),
                    E("330e PHEV 292 KM", 292, 215, 1998, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("BMW"), Name = "Seria 5", Slug = "bmw-seria-5", Generations = [
                G("F10 (2009–2016)", "bmw-5-f10", 2009, 2016,
                    E("520i 184 KM", 184, 135, 1997, ben, 9.5m, 6.0m, 7.5m), E("528i 245 KM", 245, 180, 1997, ben, 10.5m, 7.0m, 8.5m),
                    E("535i 306 KM", 306, 225, 2979, ben, 14.0m, 9.0m, 11.5m), E("M5 560 KM", 560, 412, 4395, ben, 18.0m, 11.0m, 14.0m),
                    E("520d 190 KM", 190, 140, 1995, die, 6.5m, 4.5m, 5.5m), E("530d 258 KM", 258, 190, 2993, die, 8.5m, 6.0m, 7.0m)),
                G("G30 (2016–)", "bmw-5-g30", 2016, null,
                    E("520i 184 KM", 184, 135, 1998, mild, 9.5m, 6.0m, 7.5m), E("530i 252 KM", 252, 185, 1998, mild, 10.5m, 7.0m, 8.5m),
                    E("540i 333 KM", 333, 245, 2998, mild, 14.0m, 9.0m, 11.5m), E("M5 Competition 625 KM", 625, 460, 4395, ben, 18.0m, 11.0m, 14.0m),
                    E("520d 190 KM", 190, 140, 1995, mild, 6.5m, 4.5m, 5.5m), E("530d 286 KM", 286, 210, 2993, mild, 8.5m, 6.0m, 7.0m),
                    E("530e PHEV 292 KM", 292, 215, 1998, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("BMW"), Name = "Seria 1", Slug = "bmw-seria-1", Generations = [
                G("F20 (2011–2019)", "bmw-1-f20", 2011, 2019,
                    E("116i 109 KM", 109, 80, 1499, ben, 9.5m, 6.0m, 7.5m), E("118i 136 KM", 136, 100, 1499, ben, 9.5m, 6.0m, 7.5m),
                    E("120i 184 KM", 184, 135, 1998, ben, 9.5m, 6.0m, 7.5m), E("M140i 340 KM", 340, 250, 2998, ben, 14.0m, 9.0m, 11.5m),
                    E("116d 116 KM", 116, 85, 1496, die, 5.5m, 4.0m, 4.5m), E("118d 150 KM", 150, 110, 1995, die, 6.0m, 4.2m, 5.0m)),
                G("F40 (2019–)", "bmw-1-f40", 2019, null,
                    E("116i 109 KM", 109, 80, 1499, mild, 9.5m, 6.0m, 7.5m), E("118i 136 KM", 136, 100, 1499, mild, 9.5m, 6.0m, 7.5m),
                    E("120i 178 KM", 178, 131, 1998, mild, 9.5m, 6.0m, 7.5m), E("M135i xDrive 306 KM", 306, 225, 1998, mild, 12.0m, 8.0m, 10.0m),
                    E("116d 116 KM", 116, 85, 1496, mild, 5.5m, 4.0m, 4.5m), E("118d 150 KM", 150, 110, 1995, mild, 6.0m, 4.2m, 5.0m)) ]},
            new Model { BrandId = B("BMW"), Name = "X3", Slug = "bmw-x3", Generations = [
                G("F25 (2010–2017)", "bmw-x3-f25", 2010, 2017,
                    E("xDrive20i 184 KM", 184, 135, 1997, ben, 9.5m, 6.0m, 7.5m), E("xDrive28i 245 KM", 245, 180, 1997, ben, 10.5m, 7.0m, 8.5m),
                    E("xDrive20d 184 KM", 184, 135, 1995, die, 6.5m, 4.5m, 5.5m), E("xDrive30d 258 KM", 258, 190, 2993, die, 8.5m, 6.0m, 7.0m)),
                G("G01 (2017–)", "bmw-x3-g01", 2017, null,
                    E("sDrive20i 184 KM", 184, 135, 1998, mild, 9.5m, 6.0m, 7.5m), E("xDrive30i 252 KM", 252, 185, 1998, mild, 10.5m, 7.0m, 8.5m),
                    E("M40i 360 KM", 360, 265, 2998, mild, 14.0m, 9.0m, 11.5m),
                    E("xDrive20d 190 KM", 190, 140, 1995, mild, 6.5m, 4.5m, 5.5m), E("xDrive30d 286 KM", 286, 210, 2993, mild, 8.5m, 6.0m, 7.0m),
                    E("30e PHEV 292 KM", 292, 215, 1998, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("BMW"), Name = "X5", Slug = "bmw-x5", Generations = [
                G("F15 (2013–2018)", "bmw-x5-f15", 2013, 2018,
                    E("xDrive35i 306 KM", 306, 225, 2979, ben, 14.0m, 9.0m, 11.5m), E("xDrive50i 450 KM", 450, 331, 4395, ben, 18.0m, 11.0m, 14.0m),
                    E("xDrive25d 218 KM", 218, 160, 1995, die, 7.0m, 5.0m, 5.8m), E("xDrive30d 258 KM", 258, 190, 2993, die, 8.5m, 6.0m, 7.0m),
                    E("xDrive40d 313 KM", 313, 230, 2993, die, 8.5m, 6.0m, 7.0m)),
                G("G05 (2018–)", "bmw-x5-g05", 2018, null,
                    E("xDrive40i 340 KM", 340, 250, 2998, mild, 14.0m, 9.0m, 11.5m), E("M60i 530 KM", 530, 390, 4395, mild, 18.0m, 11.0m, 14.0m),
                    E("xDrive30d 286 KM", 286, 210, 2993, mild, 8.5m, 6.0m, 7.0m), E("xDrive40d 340 KM", 340, 250, 2993, mild, 8.5m, 6.0m, 7.0m),
                    E("xDrive45e PHEV 394 KM", 394, 290, 2998, phev, 2.0m, 5.0m, 3.0m)) ]},
        ]);

        // ─── MERCEDES-BENZ ────────────────────────────────────────────────────────
        if (NeedsSeeding("Mercedes-Benz")) models.AddRange([
            new Model { BrandId = B("Mercedes-Benz"), Name = "Klasa C", Slug = "mb-klasa-c", Generations = [
                G("W204 (2007–2014)", "mb-c-w204", 2007, 2014,
                    E("C180 156 KM", 156, 115, 1796, ben, 9.5m, 6.0m, 7.5m), E("C200 184 KM", 184, 135, 1796, ben, 9.5m, 6.0m, 7.5m),
                    E("C250 204 KM", 204, 150, 1796, ben, 9.5m, 6.0m, 7.5m), E("C63 AMG 457 KM", 457, 336, 6208, ben, 18.0m, 11.0m, 14.0m),
                    E("C200d 136 KM", 136, 100, 2143, die, 6.0m, 4.2m, 5.0m), E("C220d 170 KM", 170, 125, 2143, die, 6.0m, 4.2m, 5.0m),
                    E("C250d 204 KM", 204, 150, 2143, die, 6.5m, 4.5m, 5.5m)),
                G("W205 (2014–2021)", "mb-c-w205", 2014, 2021,
                    E("C180 156 KM", 156, 115, 1595, ben, 9.5m, 6.0m, 7.5m), E("C200 184 KM", 184, 135, 1991, mild, 9.5m, 6.0m, 7.5m),
                    E("C300 258 KM", 258, 190, 1991, mild, 10.5m, 7.0m, 8.5m), E("C63 AMG 476 KM", 476, 350, 3982, ben, 18.0m, 11.0m, 14.0m),
                    E("C200d 160 KM", 160, 118, 1598, mild, 6.0m, 4.2m, 5.0m), E("C220d 194 KM", 194, 143, 1950, mild, 6.5m, 4.5m, 5.5m),
                    E("C300e PHEV 320 KM", 320, 235, 1991, phev, 2.0m, 5.0m, 3.0m)),
                G("W206 (2021–)", "mb-c-w206", 2021, null,
                    E("C180 170 KM", 170, 125, 1496, mild, 9.5m, 6.0m, 7.5m), E("C200 204 KM", 204, 150, 1496, mild, 9.5m, 6.0m, 7.5m),
                    E("C300 258 KM", 258, 190, 1999, mild, 10.5m, 7.0m, 8.5m), E("C63 AMG E 680 KM", 680, 500, 1999, phev, 2.0m, 5.0m, 3.0m),
                    E("C220d 200 KM", 200, 147, 1993, mild, 6.5m, 4.5m, 5.5m), E("C300e PHEV 313 KM", 313, 230, 1496, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("Mercedes-Benz"), Name = "Klasa E", Slug = "mb-klasa-e", Generations = [
                G("W213 (2016–2023)", "mb-e-w213", 2016, 2023,
                    E("E200 197 KM", 197, 145, 1991, mild, 9.5m, 6.0m, 7.5m), E("E300 258 KM", 258, 190, 1991, mild, 10.5m, 7.0m, 8.5m),
                    E("E450 367 KM", 367, 270, 2999, mild, 14.0m, 9.0m, 11.5m), E("E63 AMG S 612 KM", 612, 450, 3982, ben, 18.0m, 11.0m, 14.0m),
                    E("E200d 163 KM", 163, 120, 1950, mild, 6.0m, 4.2m, 5.0m), E("E220d 194 KM", 194, 143, 1950, mild, 6.5m, 4.5m, 5.5m),
                    E("E400d 340 KM", 340, 250, 2925, mild, 8.5m, 6.0m, 7.0m), E("E300e PHEV 320 KM", 320, 235, 1991, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("Mercedes-Benz"), Name = "Klasa A", Slug = "mb-klasa-a", Generations = [
                G("W176 (2012–2018)", "mb-a-w176", 2012, 2018,
                    E("A180 122 KM", 122, 90, 1595, ben, 9.5m, 6.0m, 7.5m), E("A200 156 KM", 156, 115, 1595, ben, 9.5m, 6.0m, 7.5m),
                    E("A250 211 KM", 211, 155, 1991, ben, 10.5m, 7.0m, 8.5m), E("A45 AMG 381 KM", 381, 280, 1991, ben, 14.0m, 9.0m, 11.5m),
                    E("A180d 109 KM", 109, 80, 1461, die, 5.5m, 4.0m, 4.5m), E("A200d 136 KM", 136, 100, 1461, die, 6.0m, 4.2m, 5.0m)),
                G("W177 (2018–)", "mb-a-w177", 2018, null,
                    E("A180 136 KM", 136, 100, 1332, mild, 9.5m, 6.0m, 7.5m), E("A200 163 KM", 163, 120, 1332, mild, 9.5m, 6.0m, 7.5m),
                    E("A250 224 KM", 224, 165, 1991, mild, 10.5m, 7.0m, 8.5m), E("A45 AMG S 421 KM", 421, 310, 1991, ben, 14.0m, 9.0m, 11.5m),
                    E("A180d 116 KM", 116, 85, 1461, die, 5.5m, 4.0m, 4.5m), E("A220d 190 KM", 190, 140, 1950, die, 6.5m, 4.5m, 5.5m),
                    E("A250e PHEV 218 KM", 218, 160, 1332, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("Mercedes-Benz"), Name = "GLC", Slug = "mb-glc", Generations = [
                G("X253 (2015–2022)", "mb-glc-x253", 2015, 2022,
                    E("GLC 200 197 KM", 197, 145, 1991, mild, 9.5m, 6.0m, 7.5m), E("GLC 300 258 KM", 258, 190, 1991, mild, 10.5m, 7.0m, 8.5m),
                    E("GLC 43 AMG 390 KM", 390, 287, 2996, mild, 14.0m, 9.0m, 11.5m),
                    E("GLC 200d 163 KM", 163, 120, 1950, mild, 6.0m, 4.2m, 5.0m), E("GLC 300d 245 KM", 245, 180, 1950, mild, 7.0m, 5.0m, 5.8m),
                    E("GLC 300e PHEV 320 KM", 320, 235, 1991, phev, 2.0m, 5.0m, 3.0m)),
                G("X254 (2022–)", "mb-glc-x254", 2022, null,
                    E("GLC 200 204 KM", 204, 150, 1496, mild, 9.5m, 6.0m, 7.5m), E("GLC 300 258 KM", 258, 190, 1999, mild, 10.5m, 7.0m, 8.5m),
                    E("GLC 43 AMG 421 KM", 421, 310, 2999, mild, 14.0m, 9.0m, 11.5m),
                    E("GLC 220d 197 KM", 197, 145, 1993, mild, 6.5m, 4.5m, 5.5m), E("GLC 300d 272 KM", 272, 200, 1993, mild, 7.0m, 5.0m, 5.8m),
                    E("GLC 300e PHEV 313 KM", 313, 230, 1496, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("Mercedes-Benz"), Name = "Actros", Slug = "mb-actros", Generations = [
                G("MP4 (2011–2018)", "mb-actros-mp4", 2011, 2018,
                    E("OM471 421 KM", 421, 309, 12799, die, 30.0m, 25.0m, 28.0m), E("OM471 476 KM", 476, 350, 12799, die, 30.0m, 25.0m, 28.0m),
                    E("OM471 510 KM", 510, 375, 12799, die, 30.0m, 25.0m, 28.0m), E("OM473 578 KM", 578, 425, 15600, die, 30.0m, 25.0m, 28.0m)),
                G("MP5 (2018–)", "mb-actros-mp5", 2018, null,
                    E("OM471 400 KM", 400, 294, 12799, die, 30.0m, 25.0m, 28.0m), E("OM471 449 KM", 449, 330, 12799, die, 30.0m, 25.0m, 28.0m),
                    E("OM471 476 KM", 476, 350, 12799, die, 30.0m, 25.0m, 28.0m), E("OM473 530 KM", 530, 390, 15600, die, 30.0m, 25.0m, 28.0m)) ]},
            new Model { BrandId = B("Mercedes-Benz"), Name = "Sprinter", Slug = "mb-sprinter", Generations = [
                G("W906 (2006–2018)", "mb-sprinter-w906", 2006, 2018,
                    E("211 CDI 109 KM", 109, 80, 2143, die, 12.0m, 9.0m, 10.5m), E("213 CDI 129 KM", 129, 95, 2143, die, 12.0m, 9.0m, 10.5m),
                    E("316 CDI 163 KM", 163, 120, 2143, die, 12.0m, 9.0m, 10.5m), E("319 CDI 190 KM", 190, 140, 2143, die, 12.0m, 9.0m, 10.5m)),
                G("W907 (2018–)", "mb-sprinter-w907", 2018, null,
                    E("211 CDI 114 KM", 114, 84, 1950, die, 12.0m, 9.0m, 10.5m), E("214 CDI 143 KM", 143, 105, 1950, die, 12.0m, 9.0m, 10.5m),
                    E("316 CDI 163 KM", 163, 120, 1950, die, 12.0m, 9.0m, 10.5m), E("319 CDI 190 KM", 190, 140, 1950, die, 12.0m, 9.0m, 10.5m),
                    E("eSprintersElektryczny 115 KM", 115, 85, null, ev, null, null, null)) ]},
        ]);

        // ─── TOYOTA ───────────────────────────────────────────────────────────────
        if (NeedsSeeding("Toyota")) models.AddRange([
            new Model { BrandId = B("Toyota"), Name = "Corolla", Slug = "toyota-corolla", Generations = [
                G("E210 (2018–)", "toyota-corolla-e210", 2018, null,
                    E("1.2 T 116 KM", 116, 85, 1197, ben, 7.5m, 5.0m, 6.0m), E("2.0 GR Sport 261 KM", 261, 192, 1987, ben, 12.0m, 8.0m, 10.0m),
                    E("1.8 HEV 122 KM", 122, 90, 1798, hyb, 4.5m, 5.0m, 4.8m), E("2.0 HEV 196 KM", 196, 144, 1987, hyb, 4.5m, 5.0m, 4.8m),
                    E("2.0 PHEV 223 KM", 223, 164, 1987, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("Toyota"), Name = "Yaris", Slug = "toyota-yaris", Generations = [
                G("XP150 (2011–2019)", "toyota-yaris-xp150", 2011, 2019,
                    E("1.0 VVT-i 69 KM", 69, 51, 998, ben, 8.0m, 5.5m, 6.5m), E("1.33 VVT-i 99 KM", 99, 73, 1329, ben, 8.0m, 5.5m, 6.5m),
                    E("1.4 D-4D 90 KM", 90, 66, 1364, die, 5.5m, 4.0m, 4.5m), E("1.5 HEV 100 KM", 100, 74, 1497, hyb, 4.5m, 5.0m, 4.8m)),
                G("XP210 (2019–)", "toyota-yaris-xp210", 2019, null,
                    E("1.5 HEV 116 KM", 116, 85, 1490, hyb, 4.5m, 5.0m, 4.8m), E("GR Yaris 1.6 261 KM", 261, 192, 1618, ben, 12.0m, 8.0m, 10.0m)) ]},
            new Model { BrandId = B("Toyota"), Name = "RAV4", Slug = "toyota-rav4", Generations = [
                G("IV (2012–2018)", "toyota-rav4-iv", 2012, 2018,
                    E("2.0 VVT-i 152 KM", 152, 112, 1987, ben, 9.5m, 6.0m, 7.5m), E("2.5 HEV 197 KM", 197, 145, 2494, hyb, 4.5m, 5.0m, 4.8m),
                    E("2.0 D-4D 124 KM", 124, 91, 1998, die, 6.0m, 4.2m, 5.0m), E("2.2 D-4D 150 KM", 150, 110, 2231, die, 6.0m, 4.2m, 5.0m)),
                G("V (2018–)", "toyota-rav4-v", 2018, null,
                    E("2.0 VVT-i 175 KM", 175, 129, 1987, ben, 9.5m, 6.0m, 7.5m), E("2.5 HEV 218 KM", 218, 160, 2487, hyb, 4.5m, 5.0m, 4.8m),
                    E("2.5 PHEV 306 KM", 306, 225, 2487, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("Toyota"), Name = "Camry", Slug = "toyota-camry", Generations = [
                G("XV70 (2017–)", "toyota-camry-xv70", 2017, null,
                    E("2.0 VVT-i 150 KM", 150, 110, 1987, ben, 9.5m, 6.0m, 7.5m), E("2.5 HEV 218 KM", 218, 160, 2487, hyb, 4.5m, 5.0m, 4.8m)) ]},
            new Model { BrandId = B("Toyota"), Name = "C-HR", Slug = "toyota-chr", Generations = [
                G("X10 (2016–2023)", "toyota-chr-x10", 2016, 2023,
                    E("1.2 T 116 KM", 116, 85, 1197, ben, 7.5m, 5.0m, 6.0m), E("1.8 HEV 122 KM", 122, 90, 1798, hyb, 4.5m, 5.0m, 4.8m),
                    E("2.0 HEV 184 KM", 184, 135, 1987, hyb, 4.5m, 5.0m, 4.8m)) ]},
        ]);

        // ─── FORD ─────────────────────────────────────────────────────────────────
        if (NeedsSeeding("Ford")) models.AddRange([
            new Model { BrandId = B("Ford"), Name = "Focus", Slug = "ford-focus", Generations = [
                G("Mk3 (2011–2018)", "ford-focus-mk3", 2011, 2018,
                    E("1.0 EcoBoost 100 KM", 100, 74, 999, ben, 8.0m, 5.5m, 6.5m), E("1.0 EcoBoost 125 KM", 125, 92, 999, ben, 8.0m, 5.5m, 6.5m),
                    E("1.5 EcoBoost 150 KM", 150, 110, 1499, ben, 8.5m, 5.5m, 6.5m), E("2.0 ST 250 KM", 250, 184, 1999, ben, 12.0m, 8.0m, 10.0m),
                    E("1.5 TDCi 95 KM", 95, 70, 1499, die, 5.5m, 4.0m, 4.5m), E("1.5 TDCi 120 KM", 120, 88, 1499, die, 5.5m, 4.0m, 4.5m),
                    E("2.0 TDCi 150 KM", 150, 110, 1997, die, 6.0m, 4.2m, 5.0m), E("RS 2.3 350 KM", 350, 257, 2261, ben, 12.0m, 8.0m, 10.0m)),
                G("Mk4 (2018–)", "ford-focus-mk4", 2018, null,
                    E("1.0 EcoBoost 100 KM", 100, 74, 999, mild, 8.0m, 5.5m, 6.5m), E("1.0 EcoBoost 125 KM", 125, 92, 999, mild, 8.0m, 5.5m, 6.5m),
                    E("1.5 EcoBoost 150 KM", 150, 110, 1499, mild, 8.5m, 5.5m, 6.5m), E("2.3 ST 280 KM", 280, 206, 2261, ben, 12.0m, 8.0m, 10.0m),
                    E("1.5 EcoBlue 95 KM", 95, 70, 1499, die, 5.5m, 4.0m, 4.5m), E("1.5 EcoBlue 120 KM", 120, 88, 1499, die, 5.5m, 4.0m, 4.5m),
                    E("2.0 EcoBlue 150 KM", 150, 110, 1997, die, 6.0m, 4.2m, 5.0m)) ]},
            new Model { BrandId = B("Ford"), Name = "Fiesta", Slug = "ford-fiesta", Generations = [
                G("Mk7 (2008–2017)", "ford-fiesta-mk7", 2008, 2017,
                    E("1.0 EcoBoost 100 KM", 100, 74, 999, ben, 8.0m, 5.5m, 6.5m), E("1.25 82 KM", 82, 60, 1242, ben, 9.5m, 6.0m, 7.5m),
                    E("1.6 ST 182 KM", 182, 134, 1596, ben, 9.5m, 6.0m, 7.5m), E("1.4 TDCi 70 KM", 70, 51, 1399, die, 5.5m, 4.0m, 4.5m)),
                G("Mk8 (2017–)", "ford-fiesta-mk8", 2017, null,
                    E("1.0 EcoBoost 95 KM", 95, 70, 999, ben, 8.0m, 5.5m, 6.5m), E("1.0 EcoBoost 125 KM", 125, 92, 999, ben, 8.0m, 5.5m, 6.5m),
                    E("1.5 ST 200 KM", 200, 147, 1499, ben, 9.5m, 6.0m, 7.5m), E("1.5 TDCi 85 KM", 85, 63, 1499, die, 5.5m, 4.0m, 4.5m)) ]},
            new Model { BrandId = B("Ford"), Name = "Kuga", Slug = "ford-kuga", Generations = [
                G("Mk2 (2012–2019)", "ford-kuga-mk2", 2012, 2019,
                    E("1.5 EcoBoost 150 KM", 150, 110, 1499, ben, 8.5m, 5.5m, 6.5m), E("2.0 EcoBoost 242 KM", 242, 178, 1999, ben, 10.5m, 7.0m, 8.5m),
                    E("1.5 TDCi 120 KM", 120, 88, 1499, die, 5.5m, 4.0m, 4.5m), E("2.0 TDCi 150 KM", 150, 110, 1997, die, 6.0m, 4.2m, 5.0m)),
                G("Mk3 (2019–)", "ford-kuga-mk3", 2019, null,
                    E("1.5 EcoBoost 150 KM", 150, 110, 1499, mild, 8.5m, 5.5m, 6.5m), E("2.5 PHEV 225 KM", 225, 165, 2488, phev, 2.0m, 5.0m, 3.0m),
                    E("1.5 EcoBlue 120 KM", 120, 88, 1499, die, 5.5m, 4.0m, 4.5m), E("2.0 EcoBlue 150 KM", 150, 110, 1997, die, 6.0m, 4.2m, 5.0m)) ]},
            new Model { BrandId = B("Ford"), Name = "Mustang", Slug = "ford-mustang", Generations = [
                G("S550 (2014–2022)", "ford-mustang-s550", 2014, 2022,
                    E("2.3 EcoBoost 317 KM", 317, 233, 2261, ben, 12.0m, 8.0m, 10.0m), E("5.0 V8 GT 450 KM", 450, 331, 4951, ben, 18.0m, 11.0m, 14.0m),
                    E("5.2 V8 GT500 760 KM", 760, 559, 5163, ben, 18.0m, 11.0m, 14.0m)),
                G("S650 (2023–)", "ford-mustang-s650", 2023, null,
                    E("2.3 EcoBoost 317 KM", 317, 233, 2261, ben, 12.0m, 8.0m, 10.0m), E("5.0 V8 GT 450 KM", 450, 331, 4951, ben, 18.0m, 11.0m, 14.0m)) ]},
        ]);

        // ─── OPEL ─────────────────────────────────────────────────────────────────
        if (NeedsSeeding("Opel")) models.AddRange([
            new Model { BrandId = B("Opel"), Name = "Astra", Slug = "opel-astra", Generations = [
                G("K (2015–2021)", "opel-astra-k", 2015, 2021,
                    E("1.0 Turbo 105 KM", 105, 77, 999, ben, 8.0m, 5.5m, 6.5m), E("1.4 Turbo 125 KM", 125, 92, 1399, ben, 7.5m, 5.0m, 6.0m),
                    E("1.4 Turbo 150 KM", 150, 110, 1399, ben, 7.5m, 5.0m, 6.0m), E("OPC 280 KM", 280, 206, 1998, ben, 12.0m, 8.0m, 10.0m),
                    E("1.6 CDTi 110 KM", 110, 81, 1598, die, 5.5m, 4.0m, 4.5m), E("1.6 CDTi 136 KM", 136, 100, 1598, die, 5.5m, 4.0m, 4.5m)),
                G("L (2021–)", "opel-astra-l", 2021, null,
                    E("1.2 Turbo 110 KM", 110, 81, 1199, ben, 7.5m, 5.0m, 6.0m), E("1.2 Turbo 130 KM", 130, 96, 1199, ben, 7.5m, 5.0m, 6.0m),
                    E("1.6 Hybrid 180 KM", 180, 132, 1598, hyb, 4.5m, 5.0m, 4.8m),
                    E("1.5 Diesel 130 KM", 130, 96, 1499, die, 5.5m, 4.0m, 4.5m), E("PHEV 225 KM", 225, 165, 1598, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("Opel"), Name = "Insignia", Slug = "opel-insignia", Generations = [
                G("B (2017–)", "opel-insignia-b", 2017, null,
                    E("1.5 Turbo 140 KM", 140, 103, 1490, ben, 8.5m, 5.5m, 6.5m), E("2.0 Turbo 200 KM", 200, 147, 1998, ben, 9.5m, 6.0m, 7.5m),
                    E("GSi 260 KM", 260, 191, 1998, ben, 12.0m, 8.0m, 10.0m),
                    E("1.6 CDTi 136 KM", 136, 100, 1598, die, 5.5m, 4.0m, 4.5m), E("2.0 CDTi 170 KM", 170, 125, 1997, die, 6.0m, 4.2m, 5.0m)) ]},
            new Model { BrandId = B("Opel"), Name = "Corsa", Slug = "opel-corsa", Generations = [
                G("E (2014–2019)", "opel-corsa-e", 2014, 2019,
                    E("1.0 Turbo 90 KM", 90, 66, 999, ben, 8.0m, 5.5m, 6.5m), E("1.0 Turbo 115 KM", 115, 85, 999, ben, 8.0m, 5.5m, 6.5m),
                    E("1.4 Turbo 100 KM", 100, 74, 1399, ben, 7.5m, 5.0m, 6.0m), E("OPC 207 KM", 207, 152, 1598, ben, 10.5m, 7.0m, 8.5m),
                    E("1.3 CDTi 75 KM", 75, 55, 1248, die, 5.5m, 4.0m, 4.5m), E("1.3 CDTi 95 KM", 95, 70, 1248, die, 5.5m, 4.0m, 4.5m)),
                G("F (2019–)", "opel-corsa-f", 2019, null,
                    E("1.2 75 KM", 75, 55, 1199, ben, 7.5m, 5.0m, 6.0m), E("1.2 Turbo 100 KM", 100, 74, 1199, ben, 7.5m, 5.0m, 6.0m),
                    E("1.2 Turbo 130 KM", 130, 96, 1199, ben, 7.5m, 5.0m, 6.0m), E("Corsa-e Elektryczny 136 KM", 136, 100, null, ev, null, null, null)) ]},
        ]);

        // ─── RENAULT ──────────────────────────────────────────────────────────────
        if (NeedsSeeding("Renault")) models.AddRange([
            new Model { BrandId = B("Renault"), Name = "Megane", Slug = "renault-megane", Generations = [
                G("IV (2015–)", "renault-megane-iv", 2015, null,
                    E("1.3 TCe 115 KM", 115, 85, 1332, ben, 7.5m, 5.0m, 6.0m), E("1.3 TCe 140 KM", 140, 103, 1332, ben, 7.5m, 5.0m, 6.0m),
                    E("1.8 TCe RS 300 KM", 300, 221, 1798, ben, 12.0m, 8.0m, 10.0m),
                    E("1.5 dCi 90 KM", 90, 66, 1461, die, 5.5m, 4.0m, 4.5m), E("1.5 dCi 115 KM", 115, 85, 1461, die, 5.5m, 4.0m, 4.5m),
                    E("E-Tech PHEV 160 KM", 160, 118, 1618, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("Renault"), Name = "Clio", Slug = "renault-clio", Generations = [
                G("IV (2012–2019)", "renault-clio-iv", 2012, 2019,
                    E("0.9 TCe 75 KM", 75, 55, 898, ben, 8.0m, 5.5m, 6.5m), E("0.9 TCe 90 KM", 90, 66, 898, ben, 8.0m, 5.5m, 6.5m),
                    E("1.2 TCe 120 KM", 120, 88, 1197, ben, 7.5m, 5.0m, 6.0m), E("RS 200 KM", 200, 147, 1618, ben, 9.5m, 6.0m, 7.5m),
                    E("1.5 dCi 75 KM", 75, 55, 1461, die, 5.5m, 4.0m, 4.5m), E("1.5 dCi 90 KM", 90, 66, 1461, die, 5.5m, 4.0m, 4.5m)),
                G("V (2019–)", "renault-clio-v", 2019, null,
                    E("1.0 TCe 65 KM", 65, 48, 999, ben, 8.0m, 5.5m, 6.5m), E("1.0 TCe 90 KM", 90, 66, 999, ben, 8.0m, 5.5m, 6.5m),
                    E("1.3 TCe 130 KM", 130, 96, 1332, ben, 7.5m, 5.0m, 6.0m), E("E-Tech HEV 140 KM", 140, 103, 1598, hyb, 4.5m, 5.0m, 4.8m),
                    E("1.5 dCi 85 KM", 85, 63, 1461, die, 5.5m, 4.0m, 4.5m)) ]},
            new Model { BrandId = B("Renault"), Name = "Duster", Slug = "renault-duster", Generations = [
                G("I (2010–2017)", "renault-duster-i", 2010, 2017,
                    E("1.2 TCe 125 KM", 125, 92, 1197, ben, 7.5m, 5.0m, 6.0m), E("1.6 16V 105 KM", 105, 77, 1598, ben, 9.5m, 6.0m, 7.5m),
                    E("1.5 dCi 90 KM", 90, 66, 1461, die, 5.5m, 4.0m, 4.5m), E("1.5 dCi 110 KM", 110, 81, 1461, die, 5.5m, 4.0m, 4.5m)),
                G("II (2017–)", "renault-duster-ii", 2017, null,
                    E("1.0 TCe 100 KM", 100, 74, 999, ben, 8.0m, 5.5m, 6.5m), E("1.3 TCe 150 KM", 150, 110, 1332, ben, 7.5m, 5.0m, 6.0m),
                    E("1.5 dCi 90 KM", 90, 66, 1461, die, 5.5m, 4.0m, 4.5m), E("1.5 dCi 115 KM", 115, 85, 1461, die, 5.5m, 4.0m, 4.5m)) ]},
        ]);

        // ─── HYUNDAI ──────────────────────────────────────────────────────────────
        if (NeedsSeeding("Hyundai")) models.AddRange([
            new Model { BrandId = B("Hyundai"), Name = "i30", Slug = "hyundai-i30", Generations = [
                G("PD (2016–)", "hyundai-i30-pd", 2016, null,
                    E("1.0 T-GDI 100 KM", 100, 74, 998, ben, 8.0m, 5.5m, 6.5m), E("1.0 T-GDI 120 KM", 120, 88, 998, ben, 8.0m, 5.5m, 6.5m),
                    E("1.4 T-GDI 140 KM", 140, 103, 1353, ben, 8.5m, 5.5m, 6.5m), E("N 275 KM", 275, 202, 1998, ben, 12.0m, 8.0m, 10.0m),
                    E("1.4 CRDi 90 KM", 90, 66, 1396, die, 5.5m, 4.0m, 4.5m), E("1.6 CRDi 115 KM", 115, 85, 1582, die, 5.5m, 4.0m, 4.5m)) ]},
            new Model { BrandId = B("Hyundai"), Name = "Tucson", Slug = "hyundai-tucson", Generations = [
                G("III (2015–2020)", "hyundai-tucson-iii", 2015, 2020,
                    E("1.6 T-GDI 132 KM", 132, 97, 1591, ben, 8.5m, 5.5m, 6.5m), E("1.6 T-GDI 177 KM", 177, 130, 1591, ben, 8.5m, 5.5m, 6.5m),
                    E("1.7 CRDi 116 KM", 116, 85, 1685, die, 5.5m, 4.0m, 4.5m), E("2.0 CRDi 136 KM", 136, 100, 1995, die, 6.0m, 4.2m, 5.0m),
                    E("2.0 CRDi 185 KM", 185, 136, 1995, die, 6.5m, 4.5m, 5.5m)),
                G("IV (2020–)", "hyundai-tucson-iv", 2020, null,
                    E("1.6 T-GDI 150 KM", 150, 110, 1591, mild, 8.5m, 5.5m, 6.5m), E("1.6 T-GDI Hybrid 230 KM", 230, 169, 1591, hyb, 4.5m, 5.0m, 4.8m),
                    E("1.6 CRDi 115 KM", 115, 85, 1598, mild, 5.5m, 4.0m, 4.5m), E("1.6 CRDi 136 KM", 136, 100, 1598, mild, 6.0m, 4.2m, 5.0m),
                    E("PHEV 265 KM", 265, 195, 1591, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("Hyundai"), Name = "Kona", Slug = "hyundai-kona", Generations = [
                G("I (2017–)", "hyundai-kona-i", 2017, null,
                    E("1.0 T-GDI 120 KM", 120, 88, 998, ben, 8.0m, 5.5m, 6.5m), E("1.6 T-GDI 177 KM", 177, 130, 1591, ben, 8.5m, 5.5m, 6.5m),
                    E("1.6 CRDi 115 KM", 115, 85, 1598, die, 5.5m, 4.0m, 4.5m),
                    E("Electric 39 kWh 136 KM", 136, 100, null, ev, null, null, null),
                    E("Electric 64 kWh 204 KM", 204, 150, null, ev, null, null, null)) ]},
        ]);

        // ─── KIA ──────────────────────────────────────────────────────────────────
        if (NeedsSeeding("Kia")) models.AddRange([
            new Model { BrandId = B("Kia"), Name = "Ceed", Slug = "kia-ceed", Generations = [
                G("III (2018–)", "kia-ceed-iii", 2018, null,
                    E("1.0 T-GDI 100 KM", 100, 74, 998, mild, 8.0m, 5.5m, 6.5m), E("1.0 T-GDI 120 KM", 120, 88, 998, mild, 8.0m, 5.5m, 6.5m),
                    E("1.4 T-GDI 140 KM", 140, 103, 1353, mild, 8.5m, 5.5m, 6.5m), E("GT 1.6 T-GDI 204 KM", 204, 150, 1591, ben, 10.5m, 7.0m, 8.5m),
                    E("1.4 CRDi 90 KM", 90, 66, 1396, die, 5.5m, 4.0m, 4.5m), E("1.6 CRDi 115 KM", 115, 85, 1598, die, 5.5m, 4.0m, 4.5m)) ]},
            new Model { BrandId = B("Kia"), Name = "Sportage", Slug = "kia-sportage", Generations = [
                G("IV (2015–2021)", "kia-sportage-iv", 2015, 2021,
                    E("1.6 T-GDI 132 KM", 132, 97, 1591, ben, 8.5m, 5.5m, 6.5m), E("2.0 MPI 163 KM", 163, 120, 1998, ben, 9.5m, 6.0m, 7.5m),
                    E("1.7 CRDi 115 KM", 115, 85, 1685, die, 5.5m, 4.0m, 4.5m), E("2.0 CRDi 136 KM", 136, 100, 1999, die, 6.0m, 4.2m, 5.0m),
                    E("2.0 CRDi 185 KM", 185, 136, 1999, die, 6.5m, 4.5m, 5.5m)),
                G("V (2021–)", "kia-sportage-v", 2021, null,
                    E("1.6 T-GDI 150 KM", 150, 110, 1591, mild, 8.5m, 5.5m, 6.5m), E("1.6 T-GDI HEV 230 KM", 230, 169, 1591, hyb, 4.5m, 5.0m, 4.8m),
                    E("1.6 CRDI 115 KM", 115, 85, 1598, mild, 5.5m, 4.0m, 4.5m), E("1.6 CRDI 136 KM", 136, 100, 1598, mild, 6.0m, 4.2m, 5.0m),
                    E("PHEV 265 KM", 265, 195, 1591, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("Kia"), Name = "EV6", Slug = "kia-ev6", Generations = [
                G("CV (2021–)", "kia-ev6-cv", 2021, null,
                    E("58 kWh RWD 170 KM", 170, 125, null, ev, null, null, null),
                    E("77.4 kWh RWD 229 KM", 229, 168, null, ev, null, null, null),
                    E("77.4 kWh AWD 325 KM", 325, 239, null, ev, null, null, null),
                    E("GT 77.4 kWh AWD 585 KM", 585, 430, null, ev, null, null, null)) ]},
        ]);

        // ─── DACIA ────────────────────────────────────────────────────────────────
        if (NeedsSeeding("Dacia")) models.AddRange([
            new Model { BrandId = B("Dacia"), Name = "Duster", Slug = "dacia-duster", Generations = [
                G("II (2017–2023)", "dacia-duster-ii", 2017, 2023,
                    E("1.0 TCe 90 KM", 90, 66, 999, ben, 8.0m, 5.5m, 6.5m), E("1.3 TCe 130 KM", 130, 96, 1332, ben, 8.5m, 5.5m, 6.5m),
                    E("1.5 dCi 90 KM", 90, 66, 1461, die, 5.5m, 4.0m, 4.5m), E("1.5 dCi 115 KM", 115, 85, 1461, die, 5.5m, 4.0m, 4.5m),
                    E("Bifuel LPG 100 KM", 100, 74, 999, F("LPG"), 8.0m, 5.5m, 6.5m)),
                G("III (2023–)", "dacia-duster-iii", 2023, null,
                    E("1.2 TCe 130 KM", 130, 96, 1199, ben, 8.0m, 5.5m, 6.5m), E("1.2 TCe Hybrid 140 KM", 140, 103, 1199, hyb, 4.5m, 5.0m, 4.8m),
                    E("Bifuel LPG 100 KM", 100, 74, 999, F("LPG"), 8.0m, 5.5m, 6.5m)) ]},
            new Model { BrandId = B("Dacia"), Name = "Sandero", Slug = "dacia-sandero", Generations = [
                G("III (2020–)", "dacia-sandero-iii", 2020, null,
                    E("1.0 SCe 65 KM", 65, 48, 999, ben, 8.0m, 5.5m, 6.5m), E("1.0 TCe 90 KM", 90, 66, 999, ben, 8.0m, 5.5m, 6.5m),
                    E("1.0 TCe 100 KM", 100, 74, 999, ben, 8.0m, 5.5m, 6.5m), E("Bifuel LPG 100 KM", 100, 74, 999, F("LPG"), 8.0m, 5.5m, 6.5m),
                    E("Stepway TCe 110 KM", 110, 81, 999, ben, 8.0m, 5.5m, 6.5m)) ]},
            new Model { BrandId = B("Dacia"), Name = "Spring", Slug = "dacia-spring", Generations = [
                G("I (2021–)", "dacia-spring-i", 2021, null,
                    E("Electric 27.4 kWh 65 KM", 65, 48, null, ev, null, null, null),
                    E("Electric 33 kWh 65 KM", 65, 48, null, ev, null, null, null)) ]},
        ]);

        // ─── FIAT ─────────────────────────────────────────────────────────────────
        if (NeedsSeeding("Fiat")) models.AddRange([
            new Model { BrandId = B("Fiat"), Name = "500", Slug = "fiat-500", Generations = [
                G("312 (2007–2024)", "fiat-500-312", 2007, 2024,
                    E("0.9 TwinAir 80 KM", 80, 59, 875, ben, 8.0m, 5.5m, 6.5m), E("1.0 Hybrid 70 KM", 70, 51, 999, mild, 8.0m, 5.5m, 6.5m),
                    E("1.2 69 KM", 69, 51, 1242, ben, 8.0m, 5.5m, 6.5m), E("Abarth 1.4 T-Jet 145 KM", 145, 107, 1368, ben, 8.5m, 5.5m, 6.5m)),
                G("332 elettrica (2020–)", "fiat-500e-332", 2020, null,
                    E("Electric 24 kWh 95 KM", 95, 70, null, ev, null, null, null),
                    E("Electric 42 kWh 118 KM", 118, 87, null, ev, null, null, null)) ]},
            new Model { BrandId = B("Fiat"), Name = "Panda", Slug = "fiat-panda", Generations = [
                G("319 (2011–)", "fiat-panda-319", 2011, null,
                    E("1.2 69 KM", 69, 51, 1242, ben, 8.0m, 5.5m, 6.5m), E("0.9 TwinAir 85 KM", 85, 63, 875, ben, 8.0m, 5.5m, 6.5m),
                    E("1.0 Hybrid 70 KM", 70, 51, 999, mild, 8.0m, 5.5m, 6.5m),
                    E("1.3 Multijet 75 KM", 75, 55, 1248, die, 5.5m, 4.0m, 4.5m)) ]},
            new Model { BrandId = B("Fiat"), Name = "Tipo", Slug = "fiat-tipo", Generations = [
                G("356 (2015–)", "fiat-tipo-356", 2015, null,
                    E("1.0 100 KM", 100, 74, 999, ben, 8.0m, 5.5m, 6.5m), E("1.4 95 KM", 95, 70, 1368, ben, 8.5m, 5.5m, 6.5m),
                    E("1.4 Turbo 120 KM", 120, 88, 1368, ben, 8.5m, 5.5m, 6.5m),
                    E("1.3 MultiJet 90 KM", 90, 66, 1248, die, 5.5m, 4.0m, 4.5m), E("1.6 MultiJet 120 KM", 120, 88, 1598, die, 5.5m, 4.0m, 4.5m)) ]},
        ]);

        // ─── SEAT ─────────────────────────────────────────────────────────────────
        if (NeedsSeeding("Seat")) models.AddRange([
            new Model { BrandId = B("Seat"), Name = "Leon", Slug = "seat-leon", Generations = [
                G("5F (2012–2019)", "seat-leon-5f", 2012, 2019,
                    E("1.0 TSI 115 KM", 115, 85, 999, ben, 8.0m, 5.5m, 6.5m), E("1.4 TSI 125 KM", 125, 92, 1395, ben, 8.5m, 5.5m, 6.5m),
                    E("1.5 TSI 130 KM", 130, 96, 1498, ben, 8.5m, 5.5m, 6.5m), E("Cupra 300 KM", 300, 221, 1984, ben, 12.0m, 8.0m, 10.0m),
                    E("1.6 TDI 115 KM", 115, 85, 1598, die, 5.5m, 4.0m, 4.5m), E("2.0 TDI 150 KM", 150, 110, 1968, die, 6.5m, 4.5m, 5.5m)),
                G("KL8 (2020–)", "seat-leon-kl8", 2020, null,
                    E("1.0 eTSI 110 KM", 110, 81, 999, mild, 8.0m, 5.5m, 6.5m), E("1.5 TSI 130 KM", 130, 96, 1498, mild, 8.5m, 5.5m, 6.5m),
                    E("2.0 TSI FR 190 KM", 190, 140, 1984, mild, 9.5m, 6.0m, 7.5m),
                    E("2.0 TDI 115 KM", 115, 85, 1968, die, 6.0m, 4.2m, 5.0m), E("2.0 TDI 150 KM", 150, 110, 1968, die, 6.5m, 4.5m, 5.5m),
                    E("e-Hybrid PHEV 204 KM", 204, 150, 1395, phev, 2.0m, 5.0m, 3.0m)) ]},
            new Model { BrandId = B("Seat"), Name = "Ibiza", Slug = "seat-ibiza", Generations = [
                G("V (2017–)", "seat-ibiza-v", 2017, null,
                    E("1.0 MPI 65 KM", 65, 48, 999, ben, 8.0m, 5.5m, 6.5m), E("1.0 TSI 95 KM", 95, 70, 999, ben, 8.0m, 5.5m, 6.5m),
                    E("1.0 TSI 110 KM", 110, 81, 999, ben, 8.0m, 5.5m, 6.5m), E("FR 1.5 TSI 150 KM", 150, 110, 1498, ben, 8.5m, 5.5m, 6.5m)) ]},
            new Model { BrandId = B("Seat"), Name = "Ateca", Slug = "seat-ateca", Generations = [
                G("I (2016–)", "seat-ateca-i", 2016, null,
                    E("1.0 TSI 115 KM", 115, 85, 999, ben, 8.0m, 5.5m, 6.5m), E("1.5 TSI 150 KM", 150, 110, 1498, ben, 8.5m, 5.5m, 6.5m),
                    E("2.0 TSI 190 KM", 190, 140, 1984, ben, 9.5m, 6.0m, 7.5m), E("Cupra 300 KM", 300, 221, 1984, ben, 12.0m, 8.0m, 10.0m),
                    E("1.6 TDI 115 KM", 115, 85, 1598, die, 5.5m, 4.0m, 4.5m), E("2.0 TDI 150 KM", 150, 110, 1968, die, 6.5m, 4.5m, 5.5m)) ]},
        ]);

        // ─── NISSAN ───────────────────────────────────────────────────────────────
        if (NeedsSeeding("Nissan")) models.AddRange([
            new Model { BrandId = B("Nissan"), Name = "Qashqai", Slug = "nissan-qashqai", Generations = [
                G("J11 (2013–2021)", "nissan-qashqai-j11", 2013, 2021,
                    E("1.2 DIG-T 115 KM", 115, 85, 1197, ben, 8.0m, 5.5m, 6.5m), E("1.6 DIG-T 163 KM", 163, 120, 1598, ben, 8.5m, 5.5m, 6.5m),
                    E("1.5 dCi 110 KM", 110, 81, 1461, die, 5.5m, 4.0m, 4.5m), E("1.6 dCi 130 KM", 130, 96, 1598, die, 5.5m, 4.0m, 4.5m)),
                G("J12 (2021–)", "nissan-qashqai-j12", 2021, null,
                    E("1.3 DIG-T 140 KM", 140, 103, 1332, mild, 8.5m, 5.5m, 6.5m), E("1.3 DIG-T 158 KM", 158, 116, 1332, mild, 8.5m, 5.5m, 6.5m),
                    E("e-Power HEV 190 KM", 190, 140, 1497, hyb, 4.5m, 5.0m, 4.8m)) ]},
            new Model { BrandId = B("Nissan"), Name = "Leaf", Slug = "nissan-leaf", Generations = [
                G("II (2017–)", "nissan-leaf-ii", 2017, null,
                    E("40 kWh 150 KM", 150, 110, null, ev, null, null, null),
                    E("62 kWh e+ 217 KM", 217, 160, null, ev, null, null, null)) ]},
            new Model { BrandId = B("Nissan"), Name = "Juke", Slug = "nissan-juke", Generations = [
                G("F16 (2019–)", "nissan-juke-f16", 2019, null,
                    E("1.0 DIG-T 114 KM", 114, 84, 999, ben, 8.0m, 5.5m, 6.5m),
                    E("Hybrid 143 KM", 143, 105, 1598, hyb, 4.5m, 5.0m, 4.8m)) ]},
        ]);

        // ─── HONDA ────────────────────────────────────────────────────────────────
        if (NeedsSeeding("Honda")) models.AddRange([
            new Model { BrandId = B("Honda"), Name = "Civic", Slug = "honda-civic", Generations = [
                G("X (2017–2021)", "honda-civic-x", 2017, 2021,
                    E("1.0 VTEC Turbo 126 KM", 126, 93, 988, ben, 8.0m, 5.5m, 6.5m), E("1.5 VTEC Turbo 182 KM", 182, 134, 1498, ben, 8.5m, 5.5m, 6.5m),
                    E("Type R 2.0 320 KM", 320, 235, 1996, ben, 12.0m, 8.0m, 10.0m), E("1.6 i-DTEC 120 KM", 120, 88, 1597, die, 5.5m, 4.0m, 4.5m)),
                G("XI (2021–)", "honda-civic-xi", 2021, null,
                    E("1.5 VTEC Turbo 182 KM", 182, 134, 1498, ben, 8.5m, 5.5m, 6.5m),
                    E("Type R 2.0 329 KM", 329, 242, 1996, ben, 12.0m, 8.0m, 10.0m),
                    E("e:HEV 2.0 143 KM", 143, 105, 1993, hyb, 4.5m, 5.0m, 4.8m)) ]},
            new Model { BrandId = B("Honda"), Name = "CR-V", Slug = "honda-crv", Generations = [
                G("IV (2012–2018)", "honda-crv-iv", 2012, 2018,
                    E("1.6 i-DTEC 120 KM", 120, 88, 1597, die, 5.5m, 4.0m, 4.5m), E("1.6 i-DTEC 160 KM", 160, 118, 1597, die, 5.5m, 4.0m, 4.5m),
                    E("2.0 i-VTEC 155 KM", 155, 114, 1997, ben, 9.5m, 6.0m, 7.5m)),
                G("V (2018–)", "honda-crv-v", 2018, null,
                    E("1.5 VTEC Turbo 173 KM", 173, 127, 1498, ben, 8.5m, 5.5m, 6.5m),
                    E("e:HEV 2.0 HEV 184 KM", 184, 135, 1993, hyb, 4.5m, 5.0m, 4.8m),
                    E("PHEV 325 KM", 325, 239, 2000, phev, 2.0m, 5.0m, 3.0m)) ]},
        ]);

        // ─── VOLVO ────────────────────────────────────────────────────────────────
        if (NeedsSeeding("Volvo")) models.AddRange([
            new Model { BrandId = B("Volvo"), Name = "FH", Slug = "volvo-fh", Generations = [
                G("IV (2012–2020)", "volvo-fh-iv", 2012, 2020,
                    E("D13 420 KM", 420, 309, 12777, die, 30.0m, 25.0m, 28.0m), E("D13 460 KM", 460, 338, 12777, die, 30.0m, 25.0m, 28.0m),
                    E("D13 500 KM", 500, 368, 12777, die, 30.0m, 25.0m, 28.0m), E("D16 550 KM", 550, 404, 16120, die, 30.0m, 25.0m, 28.0m)),
                G("V (2020–)", "volvo-fh-v", 2020, null,
                    E("D13 420 KM", 420, 309, 12777, die, 30.0m, 25.0m, 28.0m), E("D13 460 KM", 460, 338, 12777, die, 30.0m, 25.0m, 28.0m),
                    E("D13 500 KM", 500, 368, 12777, die, 30.0m, 25.0m, 28.0m), E("FH Electric 490 KM", 490, 360, null, ev, null, null, null)) ]},
            new Model { BrandId = B("Volvo"), Name = "FM", Slug = "volvo-fm", Generations = [
                G("IV (2012–)", "volvo-fm-iv", 2012, null,
                    E("D11 330 KM", 330, 243, 10837, die, 30.0m, 25.0m, 28.0m), E("D11 370 KM", 370, 272, 10837, die, 30.0m, 25.0m, 28.0m),
                    E("D13 430 KM", 430, 316, 12777, die, 30.0m, 25.0m, 28.0m), E("D13 460 KM", 460, 338, 12777, die, 30.0m, 25.0m, 28.0m)) ]},
        ]);

        // ─── MAN ──────────────────────────────────────────────────────────────────
        if (NeedsSeeding("MAN")) models.AddRange([
            new Model { BrandId = B("MAN"), Name = "TGX", Slug = "man-tgx", Generations = [
                G("I (2007–2020)", "man-tgx-i", 2007, 2020,
                    E("D2066 400 KM", 400, 294, 10518, die, 30.0m, 25.0m, 28.0m), E("D2066 440 KM", 440, 324, 10518, die, 30.0m, 25.0m, 28.0m),
                    E("D2676 480 KM", 480, 353, 12419, die, 30.0m, 25.0m, 28.0m), E("D2676 520 KM", 520, 382, 12419, die, 30.0m, 25.0m, 28.0m)),
                G("NEO (2020–)", "man-tgx-neo", 2020, null,
                    E("D2676 400 KM", 400, 294, 12419, die, 30.0m, 25.0m, 28.0m), E("D2676 430 KM", 430, 316, 12419, die, 30.0m, 25.0m, 28.0m),
                    E("D2676 470 KM", 470, 346, 12419, die, 30.0m, 25.0m, 28.0m), E("D3876 530 KM", 530, 390, 15249, die, 30.0m, 25.0m, 28.0m)) ]},
            new Model { BrandId = B("MAN"), Name = "TGS", Slug = "man-tgs", Generations = [
                G("I (2007–2020)", "man-tgs-i", 2007, 2020,
                    E("D2066 320 KM", 320, 235, 10518, die, 30.0m, 25.0m, 28.0m), E("D2066 360 KM", 360, 265, 10518, die, 30.0m, 25.0m, 28.0m),
                    E("D2676 400 KM", 400, 294, 12419, die, 30.0m, 25.0m, 28.0m), E("D2676 440 KM", 440, 324, 12419, die, 30.0m, 25.0m, 28.0m)),
                G("II (2020–)", "man-tgs-ii", 2020, null,
                    E("D2676 330 KM", 330, 243, 12419, die, 30.0m, 25.0m, 28.0m), E("D2676 380 KM", 380, 279, 12419, die, 30.0m, 25.0m, 28.0m),
                    E("D2676 430 KM", 430, 316, 12419, die, 30.0m, 25.0m, 28.0m)) ]},
        ]);

        // ─── SCANIA ───────────────────────────────────────────────────────────────
        if (NeedsSeeding("Scania")) models.AddRange([
            new Model { BrandId = B("Scania"), Name = "R-Series", Slug = "scania-r-series", Generations = [
                G("R5 (2009–2016)", "scania-r-r5", 2009, 2016,
                    E("DC09 320 KM", 320, 235, 9290, die, 30.0m, 25.0m, 28.0m), E("DC13 420 KM", 420, 309, 12742, die, 30.0m, 25.0m, 28.0m),
                    E("DC13 450 KM", 450, 331, 12742, die, 30.0m, 25.0m, 28.0m), E("DC16 580 KM", 580, 427, 15607, die, 30.0m, 25.0m, 28.0m)),
                G("Next Gen (2016–)", "scania-r-nextgen", 2016, null,
                    E("DC09 320 KM", 320, 235, 9290, die, 30.0m, 25.0m, 28.0m), E("DC13 410 KM", 410, 302, 12742, die, 30.0m, 25.0m, 28.0m),
                    E("DC13 460 KM", 460, 338, 12742, die, 30.0m, 25.0m, 28.0m), E("DC13 500 KM", 500, 368, 12742, die, 30.0m, 25.0m, 28.0m),
                    E("DC16 590 KM", 590, 434, 15607, die, 30.0m, 25.0m, 28.0m)) ]},
            new Model { BrandId = B("Scania"), Name = "S-Series", Slug = "scania-s-series", Generations = [
                G("Next Gen (2016–)", "scania-s-nextgen", 2016, null,
                    E("DC13 410 KM", 410, 302, 12742, die, 30.0m, 25.0m, 28.0m), E("DC13 460 KM", 460, 338, 12742, die, 30.0m, 25.0m, 28.0m),
                    E("DC13 500 KM", 500, 368, 12742, die, 30.0m, 25.0m, 28.0m), E("DC16 590 KM", 590, 434, 15607, die, 30.0m, 25.0m, 28.0m)) ]},
        ]);

        // ─── YAMAHA (motocykle) ───────────────────────────────────────────────────
        if (NeedsSeeding("Yamaha")) models.AddRange([
            new Model { BrandId = B("Yamaha"), Name = "MT-07", Slug = "yamaha-mt07", Generations = [
                G("RM04 (2013–2020)", "yamaha-mt07-rm04", 2013, 2020,
                    E("689cc CP2 73 KM", 73, 54, 689, ben, 6.0m, 4.5m, 5.0m)),
                G("RM36 (2021–)", "yamaha-mt07-rm36", 2021, null,
                    E("689cc CP2 73 KM", 73, 54, 689, ben, 6.0m, 4.5m, 5.0m)) ]},
            new Model { BrandId = B("Yamaha"), Name = "MT-09", Slug = "yamaha-mt09", Generations = [
                G("RN29 (2013–2020)", "yamaha-mt09-rn29", 2013, 2020,
                    E("847cc CP3 115 KM", 115, 85, 847, ben, 8.0m, 6.0m, 7.0m)),
                G("RN57 (2021–)", "yamaha-mt09-rn57", 2021, null,
                    E("890cc CP3 119 KM", 119, 88, 890, ben, 8.0m, 6.0m, 7.0m)) ]},
            new Model { BrandId = B("Yamaha"), Name = "Tracer 9", Slug = "yamaha-tracer9", Generations = [
                G("RN57 GT (2021–)", "yamaha-tracer9-rn57", 2021, null,
                    E("890cc CP3 119 KM", 119, 88, 890, ben, 6.5m, 5.0m, 5.8m)) ]},
            new Model { BrandId = B("Yamaha"), Name = "YZF-R1", Slug = "yamaha-r1", Generations = [
                G("RN32 (2015–)", "yamaha-r1-rn32", 2015, null,
                    E("998cc Crossplane 200 KM", 200, 147, 998, ben, 8.0m, 6.0m, 7.0m)) ]},
            new Model { BrandId = B("Yamaha"), Name = "TMAX 560", Slug = "yamaha-tmax560", Generations = [
                G("SJ19 (2020–)", "yamaha-tmax560-sj19", 2020, null,
                    E("562cc parallel twin 47 KM", 47, 35, 562, ben, 6.0m, 4.5m, 5.0m)) ]},
        ]);

        // ─── KAWASAKI ─────────────────────────────────────────────────────────────
        if (NeedsSeeding("Kawasaki")) models.AddRange([
            new Model { BrandId = B("Kawasaki"), Name = "Z900", Slug = "kawasaki-z900", Generations = [
                G("ZR900 (2017–)", "kawasaki-z900-zr900", 2017, null,
                    E("948cc inline-4 125 KM", 125, 92, 948, ben, 8.0m, 6.0m, 7.0m)) ]},
            new Model { BrandId = B("Kawasaki"), Name = "Ninja 650", Slug = "kawasaki-ninja650", Generations = [
                G("ER-6 (2017–)", "kawasaki-ninja650-er6", 2017, null,
                    E("649cc parallel twin 68 KM", 68, 50, 649, ben, 6.0m, 4.5m, 5.0m)) ]},
            new Model { BrandId = B("Kawasaki"), Name = "ZX-10R", Slug = "kawasaki-zx10r", Generations = [
                G("2021– (ZX1002L)", "kawasaki-zx10r-2021", 2021, null,
                    E("998cc inline-4 203 KM", 203, 149, 998, ben, 8.0m, 6.0m, 7.0m)) ]},
            new Model { BrandId = B("Kawasaki"), Name = "Versys 650", Slug = "kawasaki-versys650", Generations = [
                G("LE650 (2015–)", "kawasaki-versys650-le650", 2015, null,
                    E("649cc parallel twin 69 KM", 69, 51, 649, ben, 6.0m, 4.5m, 5.0m)) ]},
        ]);

        // ─── DUCATI ───────────────────────────────────────────────────────────────
        if (NeedsSeeding("Ducati")) models.AddRange([
            new Model { BrandId = B("Ducati"), Name = "Panigale V4", Slug = "ducati-panigale-v4", Generations = [
                G("2018–", "ducati-panigale-v4-2018", 2018, null,
                    E("Desmosedici Stradale 1103cc 214 KM", 214, 157, 1103, ben, 8.0m, 6.0m, 7.0m),
                    E("V4 S 1103cc 214 KM", 214, 157, 1103, ben, 8.0m, 6.0m, 7.0m),
                    E("V4 R 998cc 221 KM", 221, 163, 998, ben, 8.0m, 6.0m, 7.0m)) ]},
            new Model { BrandId = B("Ducati"), Name = "Monster", Slug = "ducati-monster", Generations = [
                G("937 (2021–)", "ducati-monster-937", 2021, null,
                    E("937cc Testastretta 111 KM", 111, 82, 937, ben, 8.0m, 6.0m, 7.0m)) ]},
            new Model { BrandId = B("Ducati"), Name = "Multistrada V4", Slug = "ducati-multistrada-v4", Generations = [
                G("2021–", "ducati-multistrada-v4-2021", 2021, null,
                    E("1158cc V4 170 KM", 170, 125, 1158, ben, 7.5m, 6.0m, 6.5m),
                    E("V4 S 1158cc 170 KM", 170, 125, 1158, ben, 7.5m, 6.0m, 6.5m)) ]},
            new Model { BrandId = B("Ducati"), Name = "Scrambler 803", Slug = "ducati-scrambler803", Generations = [
                G("2015–", "ducati-scrambler803-2015", 2015, null,
                    E("803cc Desmodue 73 KM", 73, 54, 803, ben, 6.5m, 5.0m, 5.8m)) ]},
        ]);

        // ─── TRIUMPH ──────────────────────────────────────────────────────────────
        if (NeedsSeeding("Triumph")) models.AddRange([
            new Model { BrandId = B("Triumph"), Name = "Bonneville T120", Slug = "triumph-bonneville-t120", Generations = [
                G("2016–", "triumph-bonnie-t120-2016", 2016, null,
                    E("1200cc parallel twin 80 KM", 80, 59, 1200, ben, 7.5m, 6.0m, 6.5m)) ]},
            new Model { BrandId = B("Triumph"), Name = "Street Triple 765", Slug = "triumph-street-triple-765", Generations = [
                G("2017–", "triumph-street-triple-2017", 2017, null,
                    E("765cc inline-3 R 118 KM", 118, 87, 765, ben, 8.0m, 6.0m, 7.0m),
                    E("765cc inline-3 RS 130 KM", 130, 96, 765, ben, 8.0m, 6.0m, 7.0m)) ]},
            new Model { BrandId = B("Triumph"), Name = "Tiger 900", Slug = "triumph-tiger-900", Generations = [
                G("2020–", "triumph-tiger900-2020", 2020, null,
                    E("888cc inline-3 95 KM", 95, 70, 888, ben, 6.5m, 5.0m, 5.8m)) ]},
        ]);

        // ─── HARLEY-DAVIDSON ──────────────────────────────────────────────────────
        if (NeedsSeeding("Harley-Davidson")) models.AddRange([
            new Model { BrandId = B("Harley-Davidson"), Name = "Sportster S", Slug = "hd-sportster-s", Generations = [
                G("RH1250S (2021–)", "hd-sportster-s-2021", 2021, null,
                    E("Revolution Max 1250T 121 KM", 121, 89, 1252, ben, 7.5m, 6.0m, 6.5m)) ]},
            new Model { BrandId = B("Harley-Davidson"), Name = "Fat Bob", Slug = "hd-fat-bob", Generations = [
                G("FXFBS (2017–)", "hd-fatbob-fxfbs", 2017, null,
                    E("Milwaukee-Eight 107 V-Twin 90 KM", 90, 66, 1745, ben, 7.5m, 6.0m, 6.5m),
                    E("Milwaukee-Eight 114 V-Twin 100 KM", 100, 74, 1868, ben, 7.5m, 6.0m, 6.5m)) ]},
            new Model { BrandId = B("Harley-Davidson"), Name = "Road Glide", Slug = "hd-road-glide", Generations = [
                G("2017–", "hd-road-glide-2017", 2017, null,
                    E("Milwaukee-Eight 107 90 KM", 90, 66, 1745, ben, 7.5m, 6.0m, 6.5m),
                    E("Milwaukee-Eight 114 100 KM", 100, 74, 1868, ben, 7.5m, 6.0m, 6.5m)) ]},
        ]);

        // ─── KTM ──────────────────────────────────────────────────────────────────
        if (NeedsSeeding("KTM")) models.AddRange([
            new Model { BrandId = B("KTM"), Name = "390 Duke", Slug = "ktm-390-duke", Generations = [
                G("2017–", "ktm-390duke-2017", 2017, null,
                    E("373cc single-cylinder 44 KM", 44, 32, 373, ben, 4.5m, 3.5m, 4.0m)) ]},
            new Model { BrandId = B("KTM"), Name = "790 Duke", Slug = "ktm-790-duke", Generations = [
                G("2018–", "ktm-790duke-2018", 2018, null,
                    E("799cc LC8c parallel twin 105 KM", 105, 77, 799, ben, 8.0m, 6.0m, 7.0m)) ]},
            new Model { BrandId = B("KTM"), Name = "1290 Super Duke R", Slug = "ktm-1290-super-duke-r", Generations = [
                G("2020–", "ktm-1290sdr-2020", 2020, null,
                    E("1301cc LC8 V-twin 180 KM", 180, 132, 1301, ben, 7.5m, 6.0m, 6.5m)) ]},
        ]);

        // ─── FERRARI ─────────────────────────────────────────────────────────────
        if (NeedsSeeding("Ferrari")) models.AddRange([
            new Model { BrandId = B("Ferrari"), Name = "488", Slug = "ferrari-488", Generations = [
                G("488 GTB/Spider (2015–2019)", "ferrari-488-gtb", 2015, 2019,
                    E("3.9 V8 Twin-Turbo 670 KM", 670, 493, 3902, ben)),
                G("488 Pista (2018–2019)", "ferrari-488-pista", 2018, 2019,
                    E("3.9 V8 Twin-Turbo 720 KM", 720, 529, 3902, ben)) ]},
            new Model { BrandId = B("Ferrari"), Name = "F8", Slug = "ferrari-f8", Generations = [
                G("F8 Tributo/Spider (2019–2022)", "ferrari-f8-tributo", 2019, 2022,
                    E("3.9 V8 Twin-Turbo 720 KM", 720, 529, 3902, ben)) ]},
            new Model { BrandId = B("Ferrari"), Name = "Roma", Slug = "ferrari-roma", Generations = [
                G("Roma/Spider (2019–)", "ferrari-roma-2019", 2019, null,
                    E("3.9 V8 Twin-Turbo 620 KM", 620, 456, 3855, ben)) ]},
            new Model { BrandId = B("Ferrari"), Name = "SF90 Stradale", Slug = "ferrari-sf90", Generations = [
                G("SF90 Stradale/Spider (2019–)", "ferrari-sf90-2019", 2019, null,
                    E("4.0 V8 + electric PHEV 1000 KM", 1000, 735, 3990, phev)) ]},
            new Model { BrandId = B("Ferrari"), Name = "Purosangue", Slug = "ferrari-purosangue", Generations = [
                G("F176 (2022–)", "ferrari-purosangue-2022", 2022, null,
                    E("6.5 V12 725 KM", 725, 533, 6496, ben)) ]},
        ]);

        // ─── PORSCHE ─────────────────────────────────────────────────────────────
        if (NeedsSeeding("Porsche")) models.AddRange([
            new Model { BrandId = B("Porsche"), Name = "911", Slug = "porsche-911", Generations = [
                G("991 (2011–2018)", "porsche-911-991", 2011, 2018,
                    E("3.0 T6 Carrera 370 KM", 370, 272, 2981, ben),
                    E("3.0 T6 Carrera S 420 KM", 420, 309, 2981, ben),
                    E("3.8 T6 Turbo S 580 KM", 580, 427, 3800, ben),
                    E("GT3 4.0 500 KM", 500, 368, 3996, ben)),
                G("992 (2018–)", "porsche-911-992", 2018, null,
                    E("3.0 T6 Carrera 385 KM", 385, 283, 2981, ben),
                    E("3.0 T6 Carrera S 450 KM", 450, 331, 2981, ben),
                    E("3.8 T6 Turbo S 650 KM", 650, 478, 3745, ben),
                    E("GT3 4.0 510 KM", 510, 375, 3996, ben)) ]},
            new Model { BrandId = B("Porsche"), Name = "Cayenne", Slug = "porsche-cayenne", Generations = [
                G("9YA (2017–)", "porsche-cayenne-9ya", 2017, null,
                    E("3.0 T6 340 KM", 340, 250, 2995, ben),
                    E("3.0 T6 S 440 KM", 440, 324, 2995, ben),
                    E("4.0 V8 Turbo 550 KM", 550, 404, 3996, ben),
                    E("4.0 V8 Turbo GT 640 KM", 640, 471, 3996, ben),
                    E("E-Hybrid PHEV 462 KM", 462, 340, 2995, phev)) ]},
            new Model { BrandId = B("Porsche"), Name = "Macan", Slug = "porsche-macan", Generations = [
                G("95B (2013–2023)", "porsche-macan-95b", 2013, 2023,
                    E("2.0T 245 KM", 245, 180, 1984, ben),
                    E("3.0 T6 GTS 380 KM", 380, 279, 2995, ben),
                    E("3.0 Turbo 440 KM", 440, 324, 2995, ben),
                    E("2.0 TDI 211 KM", 211, 155, 1950, die),
                    E("3.0 TDI S 258 KM", 258, 190, 2967, die)),
                G("J1 EV (2024–)", "porsche-macan-j1", 2024, null,
                    E("Electric RWD 408 KM", 408, 300, null, ev),
                    E("Electric Turbo AWD 639 KM", 639, 470, null, ev)) ]},
            new Model { BrandId = B("Porsche"), Name = "Taycan", Slug = "porsche-taycan", Generations = [
                G("Y1A (2019–)", "porsche-taycan-y1a", 2019, null,
                    E("4S 571 KM", 571, 420, null, ev),
                    E("GTS 598 KM", 598, 440, null, ev),
                    E("Turbo 680 KM", 680, 500, null, ev),
                    E("Turbo S 761 KM", 761, 560, null, ev)) ]},
            new Model { BrandId = B("Porsche"), Name = "Panamera", Slug = "porsche-panamera", Generations = [
                G("971 (2016–)", "porsche-panamera-971", 2016, null,
                    E("3.0 T6 330 KM", 330, 243, 2995, ben),
                    E("3.0 T6 4S 440 KM", 440, 324, 2995, ben),
                    E("4.0 V8 Turbo S 630 KM", 630, 463, 3996, ben),
                    E("E-Hybrid PHEV 462 KM", 462, 340, 2995, phev)) ]},
        ]);

        // ─── LAMBORGHINI ─────────────────────────────────────────────────────────
        if (NeedsSeeding("Lamborghini")) models.AddRange([
            new Model { BrandId = B("Lamborghini"), Name = "Huracán", Slug = "lamborghini-huracan", Generations = [
                G("LP610-4 (2014–2021)", "lamborghini-huracan-lp610", 2014, 2021,
                    E("5.2 V10 610 KM", 610, 449, 5204, ben)),
                G("EVO (2019–2024)", "lamborghini-huracan-evo", 2019, 2024,
                    E("5.2 V10 640 KM", 640, 471, 5204, ben)) ]},
            new Model { BrandId = B("Lamborghini"), Name = "Urus", Slug = "lamborghini-urus", Generations = [
                G("Urus (2018–)", "lamborghini-urus-2018", 2018, null,
                    E("4.0 V8 Twin-Turbo 650 KM", 650, 478, 3996, ben),
                    E("S/Performante 4.0 V8 666 KM", 666, 490, 3996, ben)) ]},
            new Model { BrandId = B("Lamborghini"), Name = "Revuelto", Slug = "lamborghini-revuelto", Generations = [
                G("LB744 (2023–)", "lamborghini-revuelto-2023", 2023, null,
                    E("6.5 V12 + electric PHEV 1001 KM", 1001, 736, 6498, phev)) ]},
        ]);

        // ─── LAND ROVER ──────────────────────────────────────────────────────────
        if (NeedsSeeding("Land Rover")) models.AddRange([
            new Model { BrandId = B("Land Rover"), Name = "Defender", Slug = "lr-defender", Generations = [
                G("L663 (2020–)", "lr-defender-l663", 2020, null,
                    E("P300 2.0T 300 KM", 300, 221, 1997, ben),
                    E("P400 3.0T 400 KM", 400, 294, 2996, ben),
                    E("D200 2.0D 200 KM", 200, 147, 1997, die),
                    E("D300 3.0D 300 KM", 300, 221, 2997, die),
                    E("P400e PHEV 404 KM", 404, 297, 1997, phev)) ]},
            new Model { BrandId = B("Land Rover"), Name = "Discovery", Slug = "lr-discovery", Generations = [
                G("L462 (2016–)", "lr-discovery-l462", 2016, null,
                    E("P300 2.0T 300 KM", 300, 221, 1997, ben),
                    E("D250 3.0D 249 KM", 249, 183, 2997, die),
                    E("D300 3.0D 300 KM", 300, 221, 2997, die)) ]},
            new Model { BrandId = B("Land Rover"), Name = "Range Rover Sport", Slug = "lr-rr-sport", Generations = [
                G("L494 (2013–2022)", "lr-rrs-l494", 2013, 2022,
                    E("P340 3.0T 340 KM", 340, 250, 2996, ben),
                    E("SVR 5.0 SC 575 KM", 575, 423, 5000, ben),
                    E("D300 3.0D 300 KM", 300, 221, 2997, die),
                    E("P400e PHEV 404 KM", 404, 297, 1997, phev)),
                G("L461 (2022–)", "lr-rrs-l461", 2022, null,
                    E("P360 3.0T 360 KM", 360, 265, 2996, ben),
                    E("P530 4.4 V8 530 KM", 530, 390, 4395, ben),
                    E("D350 3.0D 350 KM", 350, 257, 2997, die),
                    E("P510e PHEV 510 KM", 510, 375, 2997, phev)) ]},
            new Model { BrandId = B("Land Rover"), Name = "Range Rover Evoque", Slug = "lr-rr-evoque", Generations = [
                G("L538 (2011–2018)", "lr-evoque-l538", 2011, 2018,
                    E("Si4 2.0T 240 KM", 240, 177, 1998, ben),
                    E("TD4 2.0D 150 KM", 150, 110, 1999, die), E("TD4 2.0D 180 KM", 180, 132, 1999, die)),
                G("L551 (2019–)", "lr-evoque-l551", 2019, null,
                    E("P200 2.0T 200 KM", 200, 147, 1997, ben), E("P250 2.0T 249 KM", 249, 183, 1997, ben),
                    E("D150 2.0D 150 KM", 150, 110, 1998, die), E("D200 2.0D 204 KM", 204, 150, 1998, die),
                    E("P300e PHEV 300 KM", 300, 221, 1497, phev)) ]},
        ]);

        // ─── JAGUAR ───────────────────────────────────────────────────────────────
        if (NeedsSeeding("Jaguar")) models.AddRange([
            new Model { BrandId = B("Jaguar"), Name = "XE", Slug = "jaguar-xe", Generations = [
                G("X760 (2015–)", "jaguar-xe-x760", 2015, null,
                    E("P200 2.0T 200 KM", 200, 147, 1997, ben), E("P250 2.0T 250 KM", 250, 184, 1997, ben),
                    E("D150 2.0D 150 KM", 150, 110, 1998, die), E("D180 2.0D 180 KM", 180, 132, 1998, die)) ]},
            new Model { BrandId = B("Jaguar"), Name = "XF", Slug = "jaguar-xf", Generations = [
                G("X260 (2015–)", "jaguar-xf-x260", 2015, null,
                    E("P250 2.0T 250 KM", 250, 184, 1997, ben), E("P300 2.0T 300 KM", 300, 221, 1997, ben),
                    E("D165 2.0D 165 KM", 165, 121, 1998, die), E("D204 2.0D 204 KM", 204, 150, 1998, die),
                    E("D300 3.0D 300 KM", 300, 221, 2993, die)) ]},
            new Model { BrandId = B("Jaguar"), Name = "F-Pace", Slug = "jaguar-f-pace", Generations = [
                G("X761 (2016–)", "jaguar-fpace-x761", 2016, null,
                    E("P250 2.0T 250 KM", 250, 184, 1997, ben), E("P400 3.0T 400 KM", 400, 294, 2996, ben),
                    E("D165 2.0D 165 KM", 165, 121, 1998, die), E("D204 2.0D 204 KM", 204, 150, 1998, die),
                    E("P400e PHEV 404 KM", 404, 297, 1997, phev)) ]},
            new Model { BrandId = B("Jaguar"), Name = "I-Pace", Slug = "jaguar-i-pace", Generations = [
                G("X590 EV (2018–)", "jaguar-ipace-x590", 2018, null,
                    E("EV400 AWD 400 KM", 400, 294, null, ev)) ]},
            new Model { BrandId = B("Jaguar"), Name = "F-Type", Slug = "jaguar-f-type", Generations = [
                G("X152 (2012–)", "jaguar-ftype-x152", 2012, null,
                    E("2.0T 300 KM", 300, 221, 1997, ben), E("3.0 V6 340 KM", 340, 250, 2995, ben),
                    E("5.0 V8 R 450 KM", 450, 331, 5000, ben), E("SVR 5.0 V8 575 KM", 575, 423, 5000, ben)) ]},
        ]);

        // ─── TESLA ────────────────────────────────────────────────────────────────
        if (NeedsSeeding("Tesla")) models.AddRange([
            new Model { BrandId = B("Tesla"), Name = "Model 3", Slug = "tesla-model-3", Generations = [
                G("I (2017–2023)", "tesla-model3-i", 2017, 2023,
                    E("Standard Range RWD 283 KM", 283, 208, null, ev),
                    E("Long Range AWD 366 KM", 366, 269, null, ev),
                    E("Performance AWD 513 KM", 513, 377, null, ev)),
                G("Highland (2023–)", "tesla-model3-highland", 2023, null,
                    E("RWD 283 KM", 283, 208, null, ev),
                    E("Long Range AWD 366 KM", 366, 269, null, ev),
                    E("Performance AWD 460 KM", 460, 338, null, ev)) ]},
            new Model { BrandId = B("Tesla"), Name = "Model Y", Slug = "tesla-model-y", Generations = [
                G("I (2020–)", "tesla-modely-i", 2020, null,
                    E("RWD 283 KM", 283, 208, null, ev),
                    E("Long Range AWD 384 KM", 384, 282, null, ev),
                    E("Performance AWD 534 KM", 534, 393, null, ev)) ]},
            new Model { BrandId = B("Tesla"), Name = "Model S", Slug = "tesla-model-s", Generations = [
                G("Plaid (2021–)", "tesla-models-plaid", 2021, null,
                    E("Long Range AWD 670 KM", 670, 493, null, ev),
                    E("Plaid AWD 1020 KM", 1020, 750, null, ev)) ]},
            new Model { BrandId = B("Tesla"), Name = "Model X", Slug = "tesla-model-x", Generations = [
                G("Plaid (2021–)", "tesla-modelx-plaid", 2021, null,
                    E("Long Range AWD 670 KM", 670, 493, null, ev),
                    E("Plaid AWD 1020 KM", 1020, 750, null, ev)) ]},
        ]);

        // ─── CITROËN ─────────────────────────────────────────────────────────────
        if (NeedsSeeding("Citroën")) models.AddRange([
            new Model { BrandId = B("Citroën"), Name = "C3", Slug = "citroen-c3", Generations = [
                G("III (2016–)", "citroen-c3-iii", 2016, null,
                    E("1.2 PureTech 83 KM", 83, 61, 1199, ben), E("1.2 PureTech 110 KM", 110, 81, 1199, ben),
                    E("1.5 BlueHDi 100 KM", 100, 74, 1499, die)) ]},
            new Model { BrandId = B("Citroën"), Name = "C5 Aircross", Slug = "citroen-c5-aircross", Generations = [
                G("I (2018–)", "citroen-c5-aircross-i", 2018, null,
                    E("1.2 PureTech 130 KM", 130, 96, 1199, ben), E("1.6 PureTech 180 KM", 180, 132, 1598, ben),
                    E("1.5 BlueHDi 130 KM", 130, 96, 1499, die),
                    E("PHEV 225 KM", 225, 165, 1598, phev)) ]},
            new Model { BrandId = B("Citroën"), Name = "C4", Slug = "citroen-c4", Generations = [
                G("IV (2020–)", "citroen-c4-iv", 2020, null,
                    E("1.2 PureTech 100 KM", 100, 74, 1199, ben), E("1.2 PureTech 130 KM", 130, 96, 1199, ben),
                    E("1.5 BlueHDi 110 KM", 110, 81, 1499, die),
                    E("e-C4 EV 136 KM", 136, 100, null, ev)) ]},
            new Model { BrandId = B("Citroën"), Name = "Berlingo", Slug = "citroen-berlingo", Generations = [
                G("III (2018–)", "citroen-berlingo-iii", 2018, null,
                    E("1.2 PureTech 110 KM", 110, 81, 1199, ben), E("1.2 PureTech 130 KM", 130, 96, 1199, ben),
                    E("1.5 BlueHDi 100 KM", 100, 74, 1499, die), E("1.5 BlueHDi 130 KM", 130, 96, 1499, die),
                    E("e-Berlingo EV 136 KM", 136, 100, null, ev)) ]},
        ]);

        // ─── MINI ─────────────────────────────────────────────────────────────────
        if (NeedsSeeding("Mini")) models.AddRange([
            new Model { BrandId = B("Mini"), Name = "Cooper", Slug = "mini-cooper", Generations = [
                G("F55/F56 (2014–2024)", "mini-cooper-f56", 2014, 2024,
                    E("1.5 TwinPower 136 KM", 136, 100, 1499, ben),
                    E("2.0 TwinPower S 192 KM", 192, 141, 1998, ben),
                    E("JCW 2.0 231 KM", 231, 170, 1998, ben),
                    E("1.5 Cooper D 116 KM", 116, 85, 1496, die)),
                G("J01 EV (2024–)", "mini-cooper-j01", 2024, null,
                    E("C EV 184 KM", 184, 135, null, ev), E("SE EV 218 KM", 218, 160, null, ev)) ]},
            new Model { BrandId = B("Mini"), Name = "Countryman", Slug = "mini-countryman", Generations = [
                G("F60 (2017–2023)", "mini-countryman-f60", 2017, 2023,
                    E("1.5 TwinPower 136 KM", 136, 100, 1499, ben), E("2.0 TwinPower S 192 KM", 192, 141, 1998, ben),
                    E("JCW 2.0 306 KM", 306, 225, 1998, ben),
                    E("2.0 SD 190 KM", 190, 140, 1995, die),
                    E("SE PHEV 224 KM", 224, 165, 1499, phev)),
                G("U25 (2023–)", "mini-countryman-u25", 2023, null,
                    E("S 2.0T 204 KM", 204, 150, 1998, ben), E("JCW 2.0T 300 KM", 300, 221, 1998, ben),
                    E("E EV 204 KM", 204, 150, null, ev), E("SE EV AWD 313 KM", 313, 230, null, ev)) ]},
            new Model { BrandId = B("Mini"), Name = "Clubman", Slug = "mini-clubman", Generations = [
                G("F54 (2015–2024)", "mini-clubman-f54", 2015, 2024,
                    E("1.5 TwinPower 136 KM", 136, 100, 1499, ben), E("2.0 TwinPower S 192 KM", 192, 141, 1998, ben),
                    E("JCW 2.0 306 KM", 306, 225, 1998, ben),
                    E("2.0 SD 190 KM", 190, 140, 1995, die)) ]},
        ]);

        // ─── JEEP ─────────────────────────────────────────────────────────────────
        if (NeedsSeeding("Jeep")) models.AddRange([
            new Model { BrandId = B("Jeep"), Name = "Compass", Slug = "jeep-compass", Generations = [
                G("MP (2016–)", "jeep-compass-mp", 2016, null,
                    E("1.3T 130 KM", 130, 96, 1332, ben), E("1.3T 150 KM", 150, 110, 1332, ben),
                    E("2.0 Multiair 170 KM", 170, 125, 1956, ben),
                    E("1.6 Multijet 120 KM", 120, 88, 1598, die),
                    E("4xe PHEV 240 KM", 240, 177, 1332, phev)) ]},
            new Model { BrandId = B("Jeep"), Name = "Renegade", Slug = "jeep-renegade", Generations = [
                G("BU (2014–)", "jeep-renegade-bu", 2014, null,
                    E("1.0T 120 KM", 120, 88, 999, ben), E("1.3T 150 KM", 150, 110, 1332, ben),
                    E("1.6 Multijet 120 KM", 120, 88, 1598, die),
                    E("4xe PHEV 240 KM", 240, 177, 1332, phev)) ]},
            new Model { BrandId = B("Jeep"), Name = "Wrangler", Slug = "jeep-wrangler", Generations = [
                G("JL (2018–)", "jeep-wrangler-jl", 2018, null,
                    E("2.0T 272 KM", 272, 200, 1995, ben), E("3.6 V6 284 KM", 284, 209, 3604, ben),
                    E("4xe PHEV 380 KM", 380, 279, 1995, phev)) ]},
            new Model { BrandId = B("Jeep"), Name = "Grand Cherokee", Slug = "jeep-grand-cherokee", Generations = [
                G("WK2 (2010–2021)", "jeep-gc-wk2", 2010, 2021,
                    E("3.6 V6 286 KM", 286, 210, 3604, ben), E("5.7 V8 360 KM", 360, 265, 5654, ben),
                    E("3.0 CRD 250 KM", 250, 184, 2987, die)),
                G("WL (2021–)", "jeep-gc-wl", 2021, null,
                    E("3.6 V6 293 KM", 293, 215, 3604, ben),
                    E("4xe PHEV 380 KM", 380, 279, 1995, phev)) ]},
        ]);

        // ─── MAZDA ────────────────────────────────────────────────────────────────
        if (NeedsSeeding("Mazda")) models.AddRange([
            new Model { BrandId = B("Mazda"), Name = "Mazda3", Slug = "mazda-3", Generations = [
                G("BP (2018–)", "mazda3-bp", 2018, null,
                    E("2.0 Skyactiv-G 122 KM", 122, 90, 1998, ben),
                    E("2.0 e-Skyactiv-X 186 KM", 186, 137, 1998, mild),
                    E("1.8 Skyactiv-D 116 KM", 116, 85, 1756, die)) ]},
            new Model { BrandId = B("Mazda"), Name = "Mazda6", Slug = "mazda-6", Generations = [
                G("GL (2012–)", "mazda6-gl", 2012, null,
                    E("2.0 Skyactiv-G 165 KM", 165, 121, 1998, ben), E("2.5 Skyactiv-G 194 KM", 194, 143, 2488, ben),
                    E("2.2 Skyactiv-D 150 KM", 150, 110, 2191, die), E("2.2 Skyactiv-D 184 KM", 184, 135, 2191, die)) ]},
            new Model { BrandId = B("Mazda"), Name = "CX-5", Slug = "mazda-cx5", Generations = [
                G("KF (2017–)", "mazda-cx5-kf", 2017, null,
                    E("2.0 Skyactiv-G 165 KM", 165, 121, 1998, ben), E("2.5 Skyactiv-G 194 KM", 194, 143, 2488, ben),
                    E("2.5 Skyactiv-G Turbo 231 KM", 231, 170, 2488, ben),
                    E("2.2 Skyactiv-D 150 KM", 150, 110, 2191, die), E("2.2 Skyactiv-D 184 KM", 184, 135, 2191, die)) ]},
            new Model { BrandId = B("Mazda"), Name = "CX-30", Slug = "mazda-cx30", Generations = [
                G("DM (2019–)", "mazda-cx30-dm", 2019, null,
                    E("2.0 Skyactiv-G 122 KM", 122, 90, 1998, ben),
                    E("2.0 e-Skyactiv-X 186 KM", 186, 137, 1998, mild),
                    E("2.0 e-Skyactiv HEV 122 KM", 122, 90, 1998, hyb),
                    E("1.8 Skyactiv-D 116 KM", 116, 85, 1756, die)) ]},
            new Model { BrandId = B("Mazda"), Name = "MX-5", Slug = "mazda-mx5", Generations = [
                G("ND (2015–)", "mazda-mx5-nd", 2015, null,
                    E("1.5 Skyactiv-G 132 KM", 132, 97, 1496, ben),
                    E("2.0 Skyactiv-G 184 KM", 184, 135, 1998, ben)) ]},
        ]);

        // ─── MITSUBISHI ───────────────────────────────────────────────────────────
        if (NeedsSeeding("Mitsubishi")) models.AddRange([
            new Model { BrandId = B("Mitsubishi"), Name = "ASX", Slug = "mitsubishi-asx", Generations = [
                G("I (2010–2022)", "mitsubishi-asx-i", 2010, 2022,
                    E("1.6 117 KM", 117, 86, 1590, ben), E("2.0 150 KM", 150, 110, 1998, ben),
                    E("1.8 Di-D 116 KM", 116, 85, 1798, die), E("2.2 Di-D 150 KM", 150, 110, 2268, die)),
                G("II (2022–)", "mitsubishi-asx-ii", 2022, null,
                    E("1.0 Mild Hybrid 100 KM", 100, 74, 999, mild),
                    E("1.3 Mild Hybrid 140 KM", 140, 103, 1332, mild),
                    E("PHEV 1.6 180 KM", 180, 132, 1598, phev)) ]},
            new Model { BrandId = B("Mitsubishi"), Name = "Outlander", Slug = "mitsubishi-outlander", Generations = [
                G("III (2012–2021)", "mitsubishi-outlander-iii", 2012, 2021,
                    E("2.0 150 KM", 150, 110, 1998, ben), E("2.4 167 KM", 167, 123, 2360, ben),
                    E("2.2 Di-D 150 KM", 150, 110, 2268, die),
                    E("PHEV 224 KM", 224, 165, 2360, phev)),
                G("IV (2021–)", "mitsubishi-outlander-iv", 2021, null,
                    E("PHEV 2.4 302 KM", 302, 222, 2360, phev)) ]},
            new Model { BrandId = B("Mitsubishi"), Name = "Eclipse Cross", Slug = "mitsubishi-eclipse-cross", Generations = [
                G("GK (2017–)", "mitsubishi-eclipse-cross-gk", 2017, null,
                    E("1.5T 163 KM", 163, 120, 1499, ben),
                    E("PHEV 2.4 188 KM", 188, 138, 2360, phev)) ]},
            new Model { BrandId = B("Mitsubishi"), Name = "L200", Slug = "mitsubishi-l200", Generations = [
                G("V (2014–2019)", "mitsubishi-l200-v", 2014, 2019,
                    E("2.4 Di-D 154 KM", 154, 113, 2442, die), E("2.4 Di-D 178 KM", 178, 131, 2442, die)),
                G("VI (2019–)", "mitsubishi-l200-vi", 2019, null,
                    E("2.2 Di-D 150 KM", 150, 110, 2268, die)) ]},
        ]);

        // ─── SUBARU ───────────────────────────────────────────────────────────────
        if (NeedsSeeding("Subaru")) models.AddRange([
            new Model { BrandId = B("Subaru"), Name = "Forester", Slug = "subaru-forester", Generations = [
                G("V (2018–)", "subaru-forester-v", 2018, null,
                    E("2.0i e-BOXER 150 KM", 150, 110, 1995, hyb),
                    E("2.5i 184 KM", 184, 135, 2498, ben)) ]},
            new Model { BrandId = B("Subaru"), Name = "Outback", Slug = "subaru-outback", Generations = [
                G("VI (2020–)", "subaru-outback-vi", 2020, null,
                    E("2.5i 169 KM", 169, 124, 2498, ben),
                    E("2.5i e-BOXER 174 KM", 174, 128, 2498, hyb)) ]},
            new Model { BrandId = B("Subaru"), Name = "Impreza", Slug = "subaru-impreza", Generations = [
                G("V (2016–)", "subaru-impreza-v", 2016, null,
                    E("2.0i 156 KM", 156, 115, 1995, ben),
                    E("2.0i e-BOXER 150 KM", 150, 110, 1995, hyb)) ]},
            new Model { BrandId = B("Subaru"), Name = "XV", Slug = "subaru-xv", Generations = [
                G("II (2017–)", "subaru-xv-ii", 2017, null,
                    E("2.0i 156 KM", 156, 115, 1995, ben),
                    E("2.0i e-BOXER 150 KM", 150, 110, 1995, hyb)) ]},
            new Model { BrandId = B("Subaru"), Name = "WRX STI", Slug = "subaru-wrx-sti", Generations = [
                G("IV (2014–2021)", "subaru-wrx-sti-iv", 2014, 2021,
                    E("2.0 DIT 268 KM", 268, 197, 1998, ben),
                    E("STI 2.5T 304 KM", 304, 224, 2457, ben)) ]},
        ]);

        // ─── GENESIS ──────────────────────────────────────────────────────────────
        if (NeedsSeeding("Genesis")) models.AddRange([
            new Model { BrandId = B("Genesis"), Name = "G70", Slug = "genesis-g70", Generations = [
                G("I (2017–)", "genesis-g70-i", 2017, null,
                    E("2.0T 252 KM", 252, 185, 1998, ben), E("3.3T Sport 370 KM", 370, 272, 3342, ben),
                    E("2.2 CRDi 202 KM", 202, 149, 2199, die)) ]},
            new Model { BrandId = B("Genesis"), Name = "G80", Slug = "genesis-g80", Generations = [
                G("III (2020–)", "genesis-g80-iii", 2020, null,
                    E("2.5T 304 KM", 304, 224, 2497, ben), E("3.5T V6 380 KM", 380, 279, 3470, ben),
                    E("Electrified EV 365 KM", 365, 268, null, ev)) ]},
            new Model { BrandId = B("Genesis"), Name = "GV70", Slug = "genesis-gv70", Generations = [
                G("I (2021–)", "genesis-gv70-i", 2021, null,
                    E("2.5T 304 KM", 304, 224, 2497, ben), E("3.5T V6 380 KM", 380, 279, 3470, ben),
                    E("Electrified EV 490 KM", 490, 360, null, ev)) ]},
            new Model { BrandId = B("Genesis"), Name = "GV80", Slug = "genesis-gv80", Generations = [
                G("I (2020–)", "genesis-gv80-i", 2020, null,
                    E("2.5T 304 KM", 304, 224, 2497, ben), E("3.5T V6 380 KM", 380, 279, 3470, ben),
                    E("3.0D 278 KM", 278, 204, 2999, die)) ]},
        ]);

        // ─── MG ───────────────────────────────────────────────────────────────────
        if (NeedsSeeding("MG")) models.AddRange([
            new Model { BrandId = B("MG"), Name = "MG4", Slug = "mg-4", Generations = [
                G("MG4 EV (2022–)", "mg4-ev-2022", 2022, null,
                    E("51 kWh RWD 170 KM", 170, 125, null, ev),
                    E("64 kWh RWD 204 KM", 204, 150, null, ev),
                    E("77 kWh AWD 435 KM", 435, 320, null, ev)) ]},
            new Model { BrandId = B("MG"), Name = "ZS EV", Slug = "mg-zs-ev", Generations = [
                G("I (2019–)", "mg-zs-ev-i", 2019, null,
                    E("44.5 kWh 143 KM", 143, 105, null, ev),
                    E("72.6 kWh 156 KM", 156, 115, null, ev)) ]},
            new Model { BrandId = B("MG"), Name = "MG5", Slug = "mg-5", Generations = [
                G("I (2020–)", "mg5-ev-i", 2020, null,
                    E("50.3 kWh 156 KM", 156, 115, null, ev),
                    E("61 kWh 156 KM", 156, 115, null, ev)) ]},
            new Model { BrandId = B("MG"), Name = "MG3", Slug = "mg-3", Generations = [
                G("III Hybrid+ (2023–)", "mg3-hybrid-iii", 2023, null,
                    E("1.5 Hybrid 194 KM", 194, 143, 1490, hyb)) ]},
        ]);

        // ─── BYD ──────────────────────────────────────────────────────────────────
        if (NeedsSeeding("BYD")) models.AddRange([
            new Model { BrandId = B("BYD"), Name = "Atto 3", Slug = "byd-atto-3", Generations = [
                G("I (2022–)", "byd-atto3-i", 2022, null,
                    E("60.5 kWh 204 KM", 204, 150, null, ev),
                    E("60.5 kWh AWD 313 KM", 313, 230, null, ev)) ]},
            new Model { BrandId = B("BYD"), Name = "Han", Slug = "byd-han", Generations = [
                G("I (2020–)", "byd-han-i", 2020, null,
                    E("85.4 kWh RWD 272 KM", 272, 200, null, ev),
                    E("85.4 kWh AWD 517 KM", 517, 380, null, ev),
                    E("DM-i PHEV 245 KM", 245, 180, 1498, phev)) ]},
            new Model { BrandId = B("BYD"), Name = "Dolphin", Slug = "byd-dolphin", Generations = [
                G("I (2021–)", "byd-dolphin-i", 2021, null,
                    E("44.9 kWh 70 KM", 70, 51, null, ev),
                    E("60.4 kWh 204 KM", 204, 150, null, ev)) ]},
            new Model { BrandId = B("BYD"), Name = "Seal", Slug = "byd-seal", Generations = [
                G("I (2022–)", "byd-seal-i", 2022, null,
                    E("82.5 kWh RWD 313 KM", 313, 230, null, ev),
                    E("82.5 kWh AWD 530 KM", 530, 390, null, ev)) ]},
        ]);

        // ─── DODGE ────────────────────────────────────────────────────────────────
        if (NeedsSeeding("Dodge")) models.AddRange([
            new Model { BrandId = B("Dodge"), Name = "Charger", Slug = "dodge-charger", Generations = [
                G("LX (2011–2023)", "dodge-charger-lx", 2011, 2023,
                    E("3.6 V6 292 KM", 292, 215, 3604, ben), E("5.7 V8 370 KM", 370, 272, 5654, ben),
                    E("SRT Hellcat 6.2 V8 717 KM", 717, 527, 6166, ben)) ]},
            new Model { BrandId = B("Dodge"), Name = "Challenger", Slug = "dodge-challenger", Generations = [
                G("III (2008–2023)", "dodge-challenger-iii", 2008, 2023,
                    E("3.6 V6 305 KM", 305, 224, 3604, ben), E("5.7 V8 375 KM", 375, 276, 5654, ben),
                    E("SRT Hellcat 6.2 V8 717 KM", 717, 527, 6166, ben)) ]},
            new Model { BrandId = B("Dodge"), Name = "Durango", Slug = "dodge-durango", Generations = [
                G("III (2011–)", "dodge-durango-iii", 2011, null,
                    E("3.6 V6 295 KM", 295, 217, 3604, ben), E("5.7 V8 360 KM", 360, 265, 5654, ben),
                    E("SRT Hellcat 6.2 V8 710 KM", 710, 522, 6166, ben)) ]},
        ]);

        // ─── CHRYSLER ─────────────────────────────────────────────────────────────
        if (NeedsSeeding("Chrysler")) models.AddRange([
            new Model { BrandId = B("Chrysler"), Name = "300C", Slug = "chrysler-300c", Generations = [
                G("II (2011–)", "chrysler-300c-ii", 2011, null,
                    E("3.6 V6 292 KM", 292, 215, 3604, ben), E("5.7 V8 363 KM", 363, 267, 5654, ben),
                    E("SRT 6.4 V8 470 KM", 470, 346, 6424, ben),
                    E("3.0 CRD V6 239 KM", 239, 176, 2987, die)) ]},
            new Model { BrandId = B("Chrysler"), Name = "Pacifica", Slug = "chrysler-pacifica", Generations = [
                G("RU (2016–)", "chrysler-pacifica-ru", 2016, null,
                    E("3.6 V6 287 KM", 287, 211, 3604, ben),
                    E("Hybrid PHEV 260 KM", 260, 191, 3604, phev)) ]},
        ]);

        // ─── CHEVROLET ────────────────────────────────────────────────────────────
        if (NeedsSeeding("Chevrolet")) models.AddRange([
            new Model { BrandId = B("Chevrolet"), Name = "Camaro", Slug = "chevrolet-camaro", Generations = [
                G("VI (2015–2023)", "chevrolet-camaro-vi", 2015, 2023,
                    E("2.0T 275 KM", 275, 202, 1998, ben), E("3.6 V6 335 KM", 335, 246, 3649, ben),
                    E("SS 6.2 V8 453 KM", 453, 333, 6162, ben),
                    E("ZL1 6.2 SC V8 650 KM", 650, 478, 6162, ben)) ]},
            new Model { BrandId = B("Chevrolet"), Name = "Equinox", Slug = "chevrolet-equinox", Generations = [
                G("III (2017–)", "chevrolet-equinox-iii", 2017, null,
                    E("1.5T 170 KM", 170, 125, 1490, ben), E("2.0T 252 KM", 252, 185, 1998, ben),
                    E("1.6 Diesel 136 KM", 136, 100, 1598, die)) ]},
            new Model { BrandId = B("Chevrolet"), Name = "Corvette", Slug = "chevrolet-corvette", Generations = [
                G("C7 (2013–2019)", "chevrolet-corvette-c7", 2013, 2019,
                    E("6.2 V8 466 KM", 466, 343, 6162, ben),
                    E("Z06 6.2 SC V8 659 KM", 659, 485, 6162, ben)),
                G("C8 (2019–)", "chevrolet-corvette-c8", 2019, null,
                    E("6.2 V8 495 KM", 495, 364, 6162, ben),
                    E("Z06 5.5 V8 670 KM", 670, 493, 5498, ben)) ]},
        ]);

        // ─── ABARTH ───────────────────────────────────────────────────────────────
        if (NeedsSeeding("Abarth")) models.AddRange([
            new Model { BrandId = B("Abarth"), Name = "595", Slug = "abarth-595", Generations = [
                G("312 (2012–)", "abarth-595-312", 2012, null,
                    E("1.4 T-Jet 145 KM", 145, 107, 1368, ben),
                    E("Turismo 1.4 T-Jet 165 KM", 165, 121, 1368, ben),
                    E("Competizione 1.4 T-Jet 180 KM", 180, 132, 1368, ben)) ]},
            new Model { BrandId = B("Abarth"), Name = "695", Slug = "abarth-695", Generations = [
                G("695 (2019–)", "abarth-695-2019", 2019, null,
                    E("1.4 T-Jet 180 KM", 180, 132, 1368, ben),
                    E("Tributo Ferrari 1.4 T-Jet 180 KM", 180, 132, 1368, ben)) ]},
            new Model { BrandId = B("Abarth"), Name = "500e", Slug = "abarth-500e", Generations = [
                G("332 elettrica (2023–)", "abarth-500e-332", 2023, null,
                    E("Electric 154 KM", 154, 113, null, ev)) ]},
        ]);

        // ─── ALFA ROMEO ───────────────────────────────────────────────────────────
        if (NeedsSeeding("Alfa Romeo")) models.AddRange([
            new Model { BrandId = B("Alfa Romeo"), Name = "Giulia", Slug = "alfa-romeo-giulia", Generations = [
                G("952 (2016–)", "alfa-giulia-952", 2016, null,
                    E("2.0T 200 KM", 200, 147, 1995, ben), E("2.0T 280 KM", 280, 206, 1995, ben),
                    E("Quadrifoglio 2.9 V6 510 KM", 510, 375, 2891, ben),
                    E("2.2 JTD 160 KM", 160, 118, 2143, die), E("2.2 JTD 190 KM", 190, 140, 2143, die)) ]},
            new Model { BrandId = B("Alfa Romeo"), Name = "Stelvio", Slug = "alfa-romeo-stelvio", Generations = [
                G("949 (2017–)", "alfa-stelvio-949", 2017, null,
                    E("2.0T 200 KM", 200, 147, 1995, ben), E("2.0T 280 KM", 280, 206, 1995, ben),
                    E("Quadrifoglio 2.9 V6 510 KM", 510, 375, 2891, ben),
                    E("2.2 JTD 160 KM", 160, 118, 2143, die), E("2.2 JTD 210 KM", 210, 154, 2143, die)) ]},
            new Model { BrandId = B("Alfa Romeo"), Name = "Tonale", Slug = "alfa-romeo-tonale", Generations = [
                G("I (2022–)", "alfa-tonale-i", 2022, null,
                    E("1.5 MHEV 130 KM", 130, 96, 1469, mild), E("1.5 MHEV 160 KM", 160, 118, 1469, mild),
                    E("PHEV 280 KM", 280, 206, 1332, phev)) ]},
            new Model { BrandId = B("Alfa Romeo"), Name = "Giulietta", Slug = "alfa-romeo-giulietta", Generations = [
                G("940 (2010–2020)", "alfa-giulietta-940", 2010, 2020,
                    E("1.4T 120 KM", 120, 88, 1368, ben), E("1.4T 170 KM", 170, 125, 1368, ben),
                    E("1.8T QV 240 KM", 240, 177, 1742, ben),
                    E("1.6 JTDm 120 KM", 120, 88, 1598, die), E("2.0 JTDm 170 KM", 170, 125, 1956, die)) ]},
        ]);

        // ─── LEXUS ────────────────────────────────────────────────────────────────
        if (NeedsSeeding("Lexus")) models.AddRange([
            new Model { BrandId = B("Lexus"), Name = "IS", Slug = "lexus-is", Generations = [
                G("III (2013–)", "lexus-is-iii", 2013, null,
                    E("IS300h 2.5 HEV 223 KM", 223, 164, 2494, hyb),
                    E("IS200t 2.0T 245 KM", 245, 180, 1998, ben)) ]},
            new Model { BrandId = B("Lexus"), Name = "NX", Slug = "lexus-nx", Generations = [
                G("AZ10 (2014–2021)", "lexus-nx-az10", 2014, 2021,
                    E("NX200t 2.0T 238 KM", 238, 175, 1998, ben),
                    E("NX300h 2.5 HEV 197 KM", 197, 145, 2494, hyb)),
                G("AZ20 (2021–)", "lexus-nx-az20", 2021, null,
                    E("NX250 2.5 203 KM", 203, 149, 2487, ben),
                    E("NX350 2.4T 279 KM", 279, 205, 2393, ben),
                    E("NX350h HEV 243 KM", 243, 179, 2487, hyb),
                    E("NX450h+ PHEV 309 KM", 309, 227, 2487, phev)) ]},
            new Model { BrandId = B("Lexus"), Name = "RX", Slug = "lexus-rx", Generations = [
                G("IV (2015–2022)", "lexus-rx-iv", 2015, 2022,
                    E("RX300 2.0T 238 KM", 238, 175, 1998, ben),
                    E("RX450h 3.5 HEV 313 KM", 313, 230, 3456, hyb),
                    E("RX450h+ PHEV 306 KM", 306, 225, 2487, phev)),
                G("V (2022–)", "lexus-rx-v", 2022, null,
                    E("RX350 2.4T 279 KM", 279, 205, 2393, ben),
                    E("RX500h F SPORT 371 KM", 371, 273, 2393, hyb),
                    E("RX450h+ PHEV 309 KM", 309, 227, 2487, phev)) ]},
            new Model { BrandId = B("Lexus"), Name = "UX", Slug = "lexus-ux", Generations = [
                G("ZA10 (2018–)", "lexus-ux-za10", 2018, null,
                    E("UX250h 2.0 HEV 184 KM", 184, 135, 1987, hyb),
                    E("UX300e EV 204 KM", 204, 150, null, ev)) ]},
        ]);

        // ─── MASERATI ─────────────────────────────────────────────────────────────
        if (NeedsSeeding("Maserati")) models.AddRange([
            new Model { BrandId = B("Maserati"), Name = "Ghibli", Slug = "maserati-ghibli", Generations = [
                G("M157 (2013–2023)", "maserati-ghibli-m157", 2013, 2023,
                    E("3.0 V6 350 KM", 350, 257, 2979, ben), E("GTS 3.0 V6 430 KM", 430, 316, 2979, ben),
                    E("3.0 V6 D 250 KM", 250, 184, 2987, die), E("3.0 V6 D 275 KM", 275, 202, 2987, die),
                    E("Hybrid 2.0T 330 KM", 330, 243, 1995, mild)) ]},
            new Model { BrandId = B("Maserati"), Name = "Levante", Slug = "maserati-levante", Generations = [
                G("M161 (2016–)", "maserati-levante-m161", 2016, null,
                    E("3.0 V6 350 KM", 350, 257, 2979, ben), E("GTS 3.0 V6 430 KM", 430, 316, 2979, ben),
                    E("Trofeo 3.8 V8 580 KM", 580, 427, 3799, ben),
                    E("3.0 V6 D 250 KM", 250, 184, 2987, die),
                    E("Hybrid 2.0T 330 KM", 330, 243, 1995, mild)) ]},
            new Model { BrandId = B("Maserati"), Name = "GranTurismo", Slug = "maserati-granturismo", Generations = [
                G("M139 (2007–2019)", "maserati-gt-m139", 2007, 2019,
                    E("4.2 V8 405 KM", 405, 298, 4244, ben), E("4.7 V8 460 KM", 460, 338, 4691, ben)),
                G("M180 (2023–)", "maserati-gt-m180", 2023, null,
                    E("Modena 3.0 V6 490 KM", 490, 360, 2979, ben),
                    E("Trofeo 3.0 V6 550 KM", 550, 404, 2979, ben),
                    E("Folgore EV 761 KM", 761, 560, null, ev)) ]},
        ]);

        // ─── PEUGEOT ─────────────────────────────────────────────────────────────
        if (NeedsSeeding("Peugeot")) models.AddRange([
            new Model { BrandId = B("Peugeot"), Name = "208", Slug = "peugeot-208", Generations = [
                G("I (2012–2019)", "peugeot-208-i", 2012, 2019,
                    E("1.2 PureTech 82 KM", 82, 60, 1199, ben), E("1.2 PureTech 110 KM", 110, 81, 1199, ben),
                    E("GTi 1.6T 208 KM", 208, 153, 1598, ben),
                    E("1.4 HDi 70 KM", 70, 51, 1398, die), E("1.6 BlueHDi 100 KM", 100, 74, 1560, die)),
                G("II (2019–)", "peugeot-208-ii", 2019, null,
                    E("1.2 PureTech 75 KM", 75, 55, 1199, ben), E("1.2 PureTech 100 KM", 100, 74, 1199, ben),
                    E("1.2 PureTech 130 KM", 130, 96, 1199, ben),
                    E("1.5 BlueHDi 100 KM", 100, 74, 1499, die),
                    E("e-208 EV 136 KM", 136, 100, null, ev)) ]},
            new Model { BrandId = B("Peugeot"), Name = "308", Slug = "peugeot-308", Generations = [
                G("II (2013–2021)", "peugeot-308-ii", 2013, 2021,
                    E("1.2 PureTech 110 KM", 110, 81, 1199, ben), E("1.2 PureTech 130 KM", 130, 96, 1199, ben),
                    E("1.6 THP GTi 270 KM", 270, 199, 1598, ben),
                    E("1.5 BlueHDi 100 KM", 100, 74, 1499, die), E("2.0 BlueHDi 150 KM", 150, 110, 1997, die)),
                G("III (2021–)", "peugeot-308-iii", 2021, null,
                    E("1.2 PureTech 130 KM", 130, 96, 1199, ben),
                    E("1.6 Hybrid 180 KM", 180, 132, 1598, hyb),
                    E("1.5 BlueHDi 130 KM", 130, 96, 1499, die),
                    E("PHEV 225 KM", 225, 165, 1598, phev)) ]},
            new Model { BrandId = B("Peugeot"), Name = "2008", Slug = "peugeot-2008", Generations = [
                G("II (2019–)", "peugeot-2008-ii", 2019, null,
                    E("1.2 PureTech 100 KM", 100, 74, 1199, ben), E("1.2 PureTech 130 KM", 130, 96, 1199, ben),
                    E("1.5 BlueHDi 110 KM", 110, 81, 1499, die),
                    E("e-2008 EV 136 KM", 136, 100, null, ev)) ]},
            new Model { BrandId = B("Peugeot"), Name = "3008", Slug = "peugeot-3008", Generations = [
                G("II (2016–2023)", "peugeot-3008-ii", 2016, 2023,
                    E("1.2 PureTech 130 KM", 130, 96, 1199, ben), E("1.6 THP 165 KM", 165, 121, 1598, ben),
                    E("1.5 BlueHDi 130 KM", 130, 96, 1499, die), E("2.0 BlueHDi 180 KM", 180, 132, 1997, die),
                    E("PHEV 225 KM", 225, 165, 1598, phev), E("PHEV4 300 KM AWD", 300, 221, 1598, phev)),
                G("III (2023–)", "peugeot-3008-iii", 2023, null,
                    E("PHEV 195 KM", 195, 143, 1199, phev), E("PHEV 300 KM AWD", 300, 221, 1598, phev),
                    E("e-3008 EV 213 KM", 213, 157, null, ev), E("e-3008 EV Long Range 230 KM", 230, 169, null, ev)) ]},
            new Model { BrandId = B("Peugeot"), Name = "Boxer", Slug = "peugeot-boxer", Generations = [
                G("III (2006–)", "peugeot-boxer-iii", 2006, null,
                    E("2.0 BlueHDi 110 KM", 110, 81, 1997, die), E("2.0 BlueHDi 130 KM", 130, 96, 1997, die),
                    E("2.2 BlueHDi 140 KM", 140, 103, 2198, die), E("e-Boxer EV 122 KM", 122, 90, null, ev)) ]},
        ]);

        // ─── SUZUKI ───────────────────────────────────────────────────────────────
        if (NeedsSeeding("Suzuki")) models.AddRange([
            new Model { BrandId = B("Suzuki"), Name = "Swift", Slug = "suzuki-swift", Generations = [
                G("V (2017–)", "suzuki-swift-v", 2017, null,
                    E("1.0 Boosterjet 111 KM", 111, 82, 998, mild), E("1.2 DualJet 90 KM", 90, 66, 1197, hyb),
                    E("Sport 1.4 Boosterjet 129 KM", 129, 95, 1373, mild)) ]},
            new Model { BrandId = B("Suzuki"), Name = "Vitara", Slug = "suzuki-vitara", Generations = [
                G("IV (2014–)", "suzuki-vitara-iv", 2014, null,
                    E("1.4 Boosterjet 129 KM", 129, 95, 1373, mild), E("1.5 Smart Hybrid 102 KM", 102, 75, 1462, hyb),
                    E("1.4 Boosterjet SHEV 129 KM", 129, 95, 1373, mild)) ]},
            new Model { BrandId = B("Suzuki"), Name = "SX4 S-Cross", Slug = "suzuki-sx4-scross", Generations = [
                G("II (2013–)", "suzuki-sx4-scross-ii", 2013, null,
                    E("1.0 Boosterjet 112 KM", 112, 82, 998, ben), E("1.4 Boosterjet 129 KM", 129, 95, 1373, mild),
                    E("1.5 HEV 102 KM", 102, 75, 1462, hyb)) ]},
            new Model { BrandId = B("Suzuki"), Name = "Jimny", Slug = "suzuki-jimny", Generations = [
                G("IV (2018–)", "suzuki-jimny-iv", 2018, null,
                    E("1.5 102 KM", 102, 75, 1462, ben)) ]},
        ]);

        // ─── APRILIA (motocykle) ──────────────────────────────────────────────────
        if (NeedsSeeding("Aprilia")) models.AddRange([
            new Model { BrandId = B("Aprilia"), Name = "RS 660", Slug = "aprilia-rs660", Generations = [
                G("2021–", "aprilia-rs660-2021", 2021, null,
                    E("659cc parallel twin 100 KM", 100, 74, 659, ben)) ]},
            new Model { BrandId = B("Aprilia"), Name = "Tuono 660", Slug = "aprilia-tuono660", Generations = [
                G("2021–", "aprilia-tuono660-2021", 2021, null,
                    E("659cc parallel twin 95 KM", 95, 70, 659, ben)) ]},
            new Model { BrandId = B("Aprilia"), Name = "RSV4", Slug = "aprilia-rsv4", Generations = [
                G("2021–", "aprilia-rsv4-2021", 2021, null,
                    E("1099cc V4 217 KM", 217, 160, 1099, ben)) ]},
            new Model { BrandId = B("Aprilia"), Name = "Tuono V4", Slug = "aprilia-tuono-v4", Generations = [
                G("2021–", "aprilia-tuono-v4-2021", 2021, null,
                    E("1099cc V4 175 KM", 175, 129, 1099, ben)) ]},
        ]);

        // ─── MV AGUSTA (motocykle) ────────────────────────────────────────────────
        if (NeedsSeeding("MV Agusta")) models.AddRange([
            new Model { BrandId = B("MV Agusta"), Name = "Brutale 800", Slug = "mv-agusta-brutale800", Generations = [
                G("2012–", "mv-brutale800-2012", 2012, null,
                    E("798cc inline-3 140 KM", 140, 103, 798, ben)) ]},
            new Model { BrandId = B("MV Agusta"), Name = "F3 800", Slug = "mv-agusta-f3-800", Generations = [
                G("2013–", "mv-f3-800-2013", 2013, null,
                    E("798cc inline-3 148 KM", 148, 109, 798, ben)) ]},
            new Model { BrandId = B("MV Agusta"), Name = "Turismo Veloce 800", Slug = "mv-agusta-turismo-veloce", Generations = [
                G("2014–", "mv-turismo-veloce-2014", 2014, null,
                    E("798cc inline-3 110 KM", 110, 81, 798, ben)) ]},
        ]);

        // ─── ROYAL ENFIELD (motocykle) ────────────────────────────────────────────
        if (NeedsSeeding("Royal Enfield")) models.AddRange([
            new Model { BrandId = B("Royal Enfield"), Name = "Meteor 350", Slug = "re-meteor-350", Generations = [
                G("2020–", "re-meteor350-2020", 2020, null,
                    E("349cc single 20 KM", 20, 15, 349, ben)) ]},
            new Model { BrandId = B("Royal Enfield"), Name = "Himalayan", Slug = "re-himalayan", Generations = [
                G("2016–2023 (411cc)", "re-himalayan-411", 2016, 2023,
                    E("411cc single 24 KM", 24, 18, 411, ben)),
                G("2024– (450cc)", "re-himalayan-450", 2024, null,
                    E("452cc single 40 KM", 40, 29, 452, ben)) ]},
            new Model { BrandId = B("Royal Enfield"), Name = "Classic 350", Slug = "re-classic-350", Generations = [
                G("2021–", "re-classic350-2021", 2021, null,
                    E("349cc single 20 KM", 20, 15, 349, ben)) ]},
        ]);

        // ─── INDIAN (motocykle) ───────────────────────────────────────────────────
        if (NeedsSeeding("Indian")) models.AddRange([
            new Model { BrandId = B("Indian"), Name = "Scout", Slug = "indian-scout", Generations = [
                G("2015–", "indian-scout-2015", 2015, null,
                    E("1133cc V-twin 100 KM", 100, 74, 1133, ben)) ]},
            new Model { BrandId = B("Indian"), Name = "Chief", Slug = "indian-chief", Generations = [
                G("2021–", "indian-chief-2021", 2021, null,
                    E("1890cc V-twin 116 KM", 116, 85, 1890, ben)) ]},
            new Model { BrandId = B("Indian"), Name = "Challenger", Slug = "indian-challenger", Generations = [
                G("2020–", "indian-challenger-2020", 2020, null,
                    E("1768cc PowerPlus V-twin 122 KM", 122, 90, 1768, ben)) ]},
        ]);

        // ─── HUSQVARNA (motocykle) ────────────────────────────────────────────────
        if (NeedsSeeding("Husqvarna")) models.AddRange([
            new Model { BrandId = B("Husqvarna"), Name = "Svartpilen 401", Slug = "husqvarna-svartpilen-401", Generations = [
                G("2017–", "husqvarna-svartpilen401-2017", 2017, null,
                    E("373cc single 44 KM", 44, 32, 373, ben)) ]},
            new Model { BrandId = B("Husqvarna"), Name = "Vitpilen 401", Slug = "husqvarna-vitpilen-401", Generations = [
                G("2018–", "husqvarna-vitpilen401-2018", 2018, null,
                    E("373cc single 44 KM", 44, 32, 373, ben)) ]},
            new Model { BrandId = B("Husqvarna"), Name = "Norden 901", Slug = "husqvarna-norden-901", Generations = [
                G("2021–", "husqvarna-norden901-2021", 2021, null,
                    E("889cc parallel twin 105 KM", 105, 77, 889, ben)) ]},
        ]);

        // ─── BMW MOTORRAD (motocykle) ─────────────────────────────────────────────
        if (NeedsSeeding("BMW")) models.AddRange([
            new Model { BrandId = B("BMW"), Name = "R 1250 GS", Slug = "bmw-r1250gs", Generations = [
                G("2018–", "bmw-r1250gs-2018", 2018, null,
                    E("1254cc Boxer 136 KM", 136, 100, 1254, ben),
                    E("Adventure 1254cc 136 KM", 136, 100, 1254, ben)) ]},
            new Model { BrandId = B("BMW"), Name = "S 1000 RR", Slug = "bmw-s1000rr", Generations = [
                G("2019–", "bmw-s1000rr-2019", 2019, null,
                    E("999cc inline-4 210 KM", 210, 154, 999, ben)) ]},
            new Model { BrandId = B("BMW"), Name = "F 900 R", Slug = "bmw-f900r", Generations = [
                G("2020–", "bmw-f900r-2020", 2020, null,
                    E("895cc parallel twin 105 KM", 105, 77, 895, ben)) ]},
        ]);

        // ─── SUZUKI MOTORRAD (motocykle) ──────────────────────────────────────────
        if (NeedsSeeding("Suzuki")) models.AddRange([
            new Model { BrandId = B("Suzuki"), Name = "GSX-R1000", Slug = "suzuki-gsx-r1000", Generations = [
                G("L7 (2017–)", "suzuki-gsx-r1000-l7", 2017, null,
                    E("999cc inline-4 202 KM", 202, 149, 999, ben)) ]},
            new Model { BrandId = B("Suzuki"), Name = "V-Strom 1050", Slug = "suzuki-vstrom-1050", Generations = [
                G("2020–", "suzuki-vstrom1050-2020", 2020, null,
                    E("1037cc V-twin 107 KM", 107, 79, 1037, ben)) ]},
        ]);

        // ─── DAF ──────────────────────────────────────────────────────────────────
        if (NeedsSeeding("DAF")) models.AddRange([
            new Model { BrandId = B("DAF"), Name = "XF", Slug = "daf-xf", Generations = [
                G("105 (2005–2017)", "daf-xf-105", 2005, 2017,
                    E("MX-11 410 KM", 410, 302, 10837, die), E("MX-13 460 KM", 460, 338, 12902, die),
                    E("MX-13 510 KM", 510, 375, 12902, die)),
                G("XF (2017–)", "daf-xf-2017", 2017, null,
                    E("MX-11 370 KM", 370, 272, 10837, die), E("MX-11 410 KM", 410, 302, 10837, die),
                    E("MX-13 480 KM", 480, 353, 12902, die), E("MX-13 530 KM", 530, 390, 12902, die)) ]},
            new Model { BrandId = B("DAF"), Name = "XG", Slug = "daf-xg", Generations = [
                G("XG/XG+ (2021–)", "daf-xg-2021", 2021, null,
                    E("MX-11 390 KM", 390, 287, 10837, die), E("MX-13 480 KM", 480, 353, 12902, die),
                    E("MX-13 530 KM", 530, 390, 12902, die)) ]},
            new Model { BrandId = B("DAF"), Name = "LF", Slug = "daf-lf", Generations = [
                G("FA/FAR (2013–)", "daf-lf-2013", 2013, null,
                    E("PX-5 180 KM", 180, 132, 5123, die), E("PX-5 220 KM", 220, 162, 5123, die),
                    E("PX-7 250 KM", 250, 184, 6728, die), E("PX-7 290 KM", 290, 213, 6728, die)) ]},
        ]);

        // ─── IVECO ────────────────────────────────────────────────────────────────
        if (NeedsSeeding("Iveco")) models.AddRange([
            new Model { BrandId = B("Iveco"), Name = "S-Way", Slug = "iveco-s-way", Generations = [
                G("S-Way (2019–)", "iveco-s-way-2019", 2019, null,
                    E("Cursor 9 420 KM", 420, 309, 8710, die), E("Cursor 9 460 KM", 460, 338, 8710, die),
                    E("Cursor 13 500 KM", 500, 368, 12882, die), E("Cursor 13 570 KM", 570, 419, 12882, die)) ]},
            new Model { BrandId = B("Iveco"), Name = "Stralis", Slug = "iveco-stralis", Generations = [
                G("Hi-Way (2012–2019)", "iveco-stralis-hiway", 2012, 2019,
                    E("Cursor 10 360 KM", 360, 265, 10308, die), E("Cursor 10 430 KM", 430, 316, 10308, die),
                    E("Cursor 13 460 KM", 460, 338, 12882, die), E("Cursor 13 560 KM", 560, 412, 12882, die)) ]},
            new Model { BrandId = B("Iveco"), Name = "Daily", Slug = "iveco-daily", Generations = [
                G("VI (2014–)", "iveco-daily-vi", 2014, null,
                    E("2.3 HPI 116 KM", 116, 85, 2287, die), E("2.3 HPI 136 KM", 136, 100, 2287, die),
                    E("3.0 HPI 170 KM", 170, 125, 2998, die), E("3.0 HPI 210 KM", 210, 154, 2998, die),
                    E("35S e-Daily EV 200 KM", 200, 147, null, ev)) ]},
        ]);

        // ─── RENAULT TRUCKS ───────────────────────────────────────────────────────
        if (NeedsSeeding("Renault Trucks")) models.AddRange([
            new Model { BrandId = B("Renault Trucks"), Name = "T High", Slug = "rt-t-high", Generations = [
                G("T High (2013–)", "rt-t-high-2013", 2013, null,
                    E("DTI11 430 KM", 430, 316, 10837, die), E("DTI11 480 KM", 480, 353, 10837, die),
                    E("DTI13 500 KM", 500, 368, 12800, die), E("DTI13 560 KM", 560, 412, 12800, die)) ]},
            new Model { BrandId = B("Renault Trucks"), Name = "C", Slug = "rt-c", Generations = [
                G("C (2013–)", "rt-c-2013", 2013, null,
                    E("DTI8 320 KM", 320, 235, 7700, die), E("DTI11 430 KM", 430, 316, 10837, die),
                    E("DTI13 480 KM", 480, 353, 12800, die)) ]},
            new Model { BrandId = B("Renault Trucks"), Name = "D", Slug = "rt-d", Generations = [
                G("D Wide (2013–)", "rt-d-wide-2013", 2013, null,
                    E("DTI5 210 KM", 210, 154, 5100, die), E("DTI7 280 KM", 280, 206, 7700, die)) ]},
        ]);

        // ─── CASE IH ─────────────────────────────────────────────────────────────
        if (NeedsSeeding("Case IH")) models.AddRange([
            new Model { BrandId = B("Case IH"), Name = "Puma", Slug = "case-ih-puma", Generations = [
                G("CVX (2014–)", "case-ih-puma-cvx", 2014, null,
                    E("Puma 150 CVX 150 KM", 150, 110, 6728, die), E("Puma 185 CVX 185 KM", 185, 136, 6728, die),
                    E("Puma 220 CVX 220 KM", 220, 162, 6728, die), E("Puma 240 CVX 240 KM", 240, 177, 6728, die)) ]},
            new Model { BrandId = B("Case IH"), Name = "Optum", Slug = "case-ih-optum", Generations = [
                G("AFS Connect (2016–)", "case-ih-optum-afs", 2016, null,
                    E("Optum 250 CVX 250 KM", 250, 184, 8700, die), E("Optum 270 CVX 270 KM", 270, 199, 8700, die),
                    E("Optum 300 CVX 300 KM", 300, 221, 8700, die)) ]},
            new Model { BrandId = B("Case IH"), Name = "Maxxum", Slug = "case-ih-maxxum", Generations = [
                G("AFS Connect (2017–)", "case-ih-maxxum-afs", 2017, null,
                    E("Maxxum 115 115 KM", 115, 85, 4485, die), E("Maxxum 135 135 KM", 135, 99, 4485, die),
                    E("Maxxum 150 150 KM", 150, 110, 6728, die)) ]},
        ]);

        // ─── CLAAS ────────────────────────────────────────────────────────────────
        if (NeedsSeeding("Claas")) models.AddRange([
            new Model { BrandId = B("Claas"), Name = "Axion 900", Slug = "claas-axion-900", Generations = [
                G("CIS+ (2015–)", "claas-axion900-cis", 2015, null,
                    E("Axion 920 205 KM", 205, 151, 6800, die), E("Axion 940 245 KM", 245, 180, 6800, die),
                    E("Axion 960 290 KM", 290, 213, 6800, die)) ]},
            new Model { BrandId = B("Claas"), Name = "Axion 800", Slug = "claas-axion-800", Generations = [
                G("CIS+ (2012–)", "claas-axion800-cis", 2012, null,
                    E("Axion 810 180 KM", 180, 132, 6800, die), E("Axion 840 205 KM", 205, 151, 6800, die)) ]},
            new Model { BrandId = B("Claas"), Name = "Arion 600", Slug = "claas-arion-600", Generations = [
                G("CIS (2012–)", "claas-arion600-cis", 2012, null,
                    E("Arion 610 115 KM", 115, 85, 4485, die), E("Arion 640 155 KM", 155, 114, 4485, die),
                    E("Arion 660 185 KM", 185, 136, 6728, die)) ]},
        ]);

        // ─── KUBOTA ───────────────────────────────────────────────────────────────
        if (NeedsSeeding("Kubota")) models.AddRange([
            new Model { BrandId = B("Kubota"), Name = "M7", Slug = "kubota-m7", Generations = [
                G("M7-151 (2014–)", "kubota-m7-2014", 2014, null,
                    E("V6108 152 KM", 152, 112, 6108, die), E("V6108 172 KM", 172, 126, 6108, die),
                    E("V6108 192 KM", 192, 141, 6108, die)) ]},
            new Model { BrandId = B("Kubota"), Name = "M5", Slug = "kubota-m5", Generations = [
                G("M5-091 (2015–)", "kubota-m5-2015", 2015, null,
                    E("V3307 91 KM", 91, 67, 3307, die), E("V3307 111 KM", 111, 82, 3307, die)) ]},
            new Model { BrandId = B("Kubota"), Name = "L Series", Slug = "kubota-l-series", Generations = [
                G("L2502 (2016–)", "kubota-l2502-2016", 2016, null,
                    E("D1703 25 KM", 25, 18, 1703, die), E("D2703 47 KM", 47, 35, 2703, die)) ]},
        ]);

        // ─── MASSEY FERGUSON ──────────────────────────────────────────────────────
        if (NeedsSeeding("Massey Ferguson")) models.AddRange([
            new Model { BrandId = B("Massey Ferguson"), Name = "MF 5700 S", Slug = "mf-5700-s", Generations = [
                G("S (2014–)", "mf-5700s-2014", 2014, null,
                    E("5710 S 100 KM", 100, 74, 4400, die), E("5713 S 130 KM", 130, 96, 4485, die),
                    E("5715 S 155 KM", 155, 114, 4485, die)) ]},
            new Model { BrandId = B("Massey Ferguson"), Name = "MF 7700 S", Slug = "mf-7700-s", Generations = [
                G("S (2012–)", "mf-7700s-2012", 2012, null,
                    E("7715 S 155 KM", 155, 114, 6600, die), E("7720 S 205 KM", 205, 151, 6600, die),
                    E("7726 S 260 KM", 260, 191, 8400, die)) ]},
            new Model { BrandId = B("Massey Ferguson"), Name = "MF 8700 S", Slug = "mf-8700-s", Generations = [
                G("S (2017–)", "mf-8700s-2017", 2017, null,
                    E("8730 S 305 KM", 305, 224, 8400, die), E("8737 S 370 KM", 370, 272, 8400, die)) ]},
        ]);

        // ─── ZETOR ────────────────────────────────────────────────────────────────
        if (NeedsSeeding("Zetor")) models.AddRange([
            new Model { BrandId = B("Zetor"), Name = "Major", Slug = "zetor-major", Generations = [
                G("CL (2012–)", "zetor-major-cl", 2012, null,
                    E("Major CL 80 80 KM", 80, 59, 3792, die), E("Major CL 100 100 KM", 100, 74, 3792, die)) ]},
            new Model { BrandId = B("Zetor"), Name = "Crystal", Slug = "zetor-crystal", Generations = [
                G("170 HD (2016–)", "zetor-crystal-170", 2016, null,
                    E("Crystal 160 HD 162 KM", 162, 119, 7700, die)) ]},
            new Model { BrandId = B("Zetor"), Name = "Forterra", Slug = "zetor-forterra", Generations = [
                G("HD (2012–)", "zetor-forterra-hd", 2012, null,
                    E("Forterra 100 HD 100 KM", 100, 74, 4156, die), E("Forterra 130 HD 130 KM", 130, 96, 4156, die)) ]},
        ]);

        // ─── CATERPILLAR (budowlane/maszyny) ──────────────────────────────────────
        if (NeedsSeeding("Caterpillar")) models.AddRange([
            new Model { BrandId = B("Caterpillar"), Name = "CAT 320", Slug = "cat-320", Generations = [
                G("CAT 320 (2019–)", "cat-320-2019", 2019, null,
                    E("C7.1 ACERT 148 KM", 148, 109, 7100, die)) ]},
            new Model { BrandId = B("Caterpillar"), Name = "CAT 950", Slug = "cat-950", Generations = [
                G("GC/M (2015–)", "cat-950-2015", 2015, null,
                    E("C7.1 201 KM", 201, 148, 7100, die)) ]},
            new Model { BrandId = B("Caterpillar"), Name = "CAT D6", Slug = "cat-d6", Generations = [
                G("XE/XL (2016–)", "cat-d6-2016", 2016, null,
                    E("C9.3B ACERT 211 KM", 211, 155, 9300, die)) ]},
            new Model { BrandId = B("Caterpillar"), Name = "CAT 432", Slug = "cat-432", Generations = [
                G("F2 (2015–)", "cat-432-f2", 2015, null,
                    E("C3.3B 100 KM", 100, 74, 3300, die)) ]},
        ]);

        // ─── JCB (budowlane/maszyny) ───────────────────────────────────────────────
        if (NeedsSeeding("JCB")) models.AddRange([
            new Model { BrandId = B("JCB"), Name = "3CX", Slug = "jcb-3cx", Generations = [
                G("4T4 (2013–)", "jcb-3cx-4t4", 2013, null,
                    E("JCB EcoMAX 100 KM", 100, 74, 4400, die)) ]},
            new Model { BrandId = B("JCB"), Name = "JS220", Slug = "jcb-js220", Generations = [
                G("LC (2014–)", "jcb-js220-lc", 2014, null,
                    E("JCB DieselMAX 156 KM", 156, 115, 6700, die)) ]},
            new Model { BrandId = B("JCB"), Name = "525-60", Slug = "jcb-525-60", Generations = [
                G("T4 (2015–)", "jcb-525-60-t4", 2015, null,
                    E("JCB EcoMAX 55 KM", 55, 40, 2200, die)) ]},
            new Model { BrandId = B("JCB"), Name = "Fastrac 4000", Slug = "jcb-fastrac-4000", Generations = [
                G("4220 (2016–)", "jcb-fastrac-4220", 2016, null,
                    E("JCB EcoMAX 220 KM", 220, 162, 6700, die)) ]},
        ]);

        // ─── KOMATSU (budowlane/maszyny) ──────────────────────────────────────────
        if (NeedsSeeding("Komatsu")) models.AddRange([
            new Model { BrandId = B("Komatsu"), Name = "PC210", Slug = "komatsu-pc210", Generations = [
                G("LC-11 (2017–)", "komatsu-pc210-lc11", 2017, null,
                    E("SAA6D107E-3 148 KM", 148, 109, 6690, die)) ]},
            new Model { BrandId = B("Komatsu"), Name = "WA320", Slug = "komatsu-wa320", Generations = [
                G("8 (2019–)", "komatsu-wa320-8", 2019, null,
                    E("SAA6D107E-3 155 KM", 155, 114, 6690, die)) ]},
            new Model { BrandId = B("Komatsu"), Name = "D65", Slug = "komatsu-d65", Generations = [
                G("PX-18 (2018–)", "komatsu-d65-px18", 2018, null,
                    E("SAA6D114E-6 215 KM", 215, 158, 8280, die)) ]},
        ]);

        // ─── LIEBHERR (budowlane/maszyny) ─────────────────────────────────────────
        if (NeedsSeeding("Liebherr")) models.AddRange([
            new Model { BrandId = B("Liebherr"), Name = "LTM 1060", Slug = "liebherr-ltm-1060", Generations = [
                G("4.2 (2018–)", "liebherr-ltm1060-4-2", 2018, null,
                    E("OM 471 340 KM", 340, 250, 12800, die)) ]},
            new Model { BrandId = B("Liebherr"), Name = "PR 736", Slug = "liebherr-pr-736", Generations = [
                G("Litronic (2017–)", "liebherr-pr736-litronic", 2017, null,
                    E("D9512 244 KM", 244, 179, 9512, die)) ]},
            new Model { BrandId = B("Liebherr"), Name = "LB 28", Slug = "liebherr-lb-28", Generations = [
                G("LB28 (2014–)", "liebherr-lb28-2014", 2014, null,
                    E("D916 170 KM", 170, 125, 5680, die)) ]},
        ]);

        // ─── BOBCAT (budowlane) ────────────────────────────────────────────────────
        if (NeedsSeeding("Bobcat")) models.AddRange([
            new Model { BrandId = B("Bobcat"), Name = "E85", Slug = "bobcat-e85", Generations = [
                G("E85 (2016–)", "bobcat-e85-2016", 2016, null,
                    E("D34 73 KM", 73, 54, 3400, die)) ]},
            new Model { BrandId = B("Bobcat"), Name = "T650", Slug = "bobcat-t650", Generations = [
                G("T650 (2014–)", "bobcat-t650-2014", 2014, null,
                    E("D34 74 KM", 74, 54, 3400, die)) ]},
            new Model { BrandId = B("Bobcat"), Name = "S850", Slug = "bobcat-s850", Generations = [
                G("S850 (2019–)", "bobcat-s850-2019", 2019, null,
                    E("D34 96 KM", 96, 71, 3400, die)) ]},
        ]);

        // ─── TAKEUCHI (budowlane) ──────────────────────────────────────────────────
        if (NeedsSeeding("Takeuchi")) models.AddRange([
            new Model { BrandId = B("Takeuchi"), Name = "TB216", Slug = "takeuchi-tb216", Generations = [
                G("TB216 (2020–)", "takeuchi-tb216-2020", 2020, null,
                    E("Yanmar 3TNV70 15 KM", 15, 11, 854, die)) ]},
            new Model { BrandId = B("Takeuchi"), Name = "TB260", Slug = "takeuchi-tb260", Generations = [
                G("TB260 (2017–)", "takeuchi-tb260-2017", 2017, null,
                    E("Yanmar 4TNV106 56 KM", 56, 41, 3318, die)) ]},
        ]);

        // ─── WACKER NEUSON (budowlane) ────────────────────────────────────────────
        if (NeedsSeeding("Wacker Neuson")) models.AddRange([
            new Model { BrandId = B("Wacker Neuson"), Name = "EW100", Slug = "wacker-neuson-ew100", Generations = [
                G("EW100 (2017–)", "wacker-neuson-ew100-2017", 2017, null,
                    E("Perkins 74.5 KM", 75, 55, 3400, die)) ]},
            new Model { BrandId = B("Wacker Neuson"), Name = "EZ80", Slug = "wacker-neuson-ez80", Generations = [
                G("EZ80 (2017–)", "wacker-neuson-ez80-2017", 2017, null,
                    E("Kubota 64 KM", 64, 47, 2434, die)) ]},
        ]);

        // ─── DOOSAN (budowlane/maszyny) ───────────────────────────────────────────
        if (NeedsSeeding("Doosan")) models.AddRange([
            new Model { BrandId = B("Doosan"), Name = "DX225LC", Slug = "doosan-dx225lc", Generations = [
                G("DX225LC-5 (2014–)", "doosan-dx225lc-5", 2014, null,
                    E("DL06P 149 KM", 149, 110, 5890, die)) ]},
            new Model { BrandId = B("Doosan"), Name = "DX530LC", Slug = "doosan-dx530lc", Generations = [
                G("DX530LC-5 (2015–)", "doosan-dx530lc-5", 2015, null,
                    E("DL12 380 KM", 380, 279, 11100, die)) ]},
        ]);

        // ─── HITACHI CONSTRUCTION (budowlane/maszyny) ─────────────────────────────
        if (NeedsSeeding("Hitachi Construction")) models.AddRange([
            new Model { BrandId = B("Hitachi Construction"), Name = "ZX210LC", Slug = "hitachi-zx210lc", Generations = [
                G("ZX210LC-6 (2014–)", "hitachi-zx210lc-6", 2014, null,
                    E("Hino J05E 148 KM", 148, 109, 4964, die)) ]},
            new Model { BrandId = B("Hitachi Construction"), Name = "ZX470LC", Slug = "hitachi-zx470lc", Generations = [
                G("ZX470LC-6 (2015–)", "hitachi-zx470lc-6", 2015, null,
                    E("Isuzu 6HK1 322 KM", 322, 237, 7790, die)) ]},
        ]);

        // ─── TEREX (budowlane/maszyny) ────────────────────────────────────────────
        if (NeedsSeeding("Terex")) models.AddRange([
            new Model { BrandId = B("Terex"), Name = "TC50", Slug = "terex-tc50", Generations = [
                G("TC50 (2014–)", "terex-tc50-2014", 2014, null,
                    E("Perkins 35.6 KM", 36, 26, 1496, die)) ]},
            new Model { BrandId = B("Terex"), Name = "AC 100-4", Slug = "terex-ac100-4", Generations = [
                G("AC 100-4 (2016–)", "terex-ac100-4-2016", 2016, null,
                    E("Mercedes OM936 354 KM", 354, 260, 7700, die)) ]},
        ]);

        // ─── HUMBAUR (przyczepy) ──────────────────────────────────────────────────
        if (NeedsSeeding("Humbaur")) models.AddRange([
            new Model { BrandId = B("Humbaur"), Name = "Przyczepa jednoosiowa", Slug = "humbaur-1os", Generations = [
                G("HUK (2015–)", "humbaur-huk-2015", 2015, null) ]},
            new Model { BrandId = B("Humbaur"), Name = "Przyczepa dwuosiowa", Slug = "humbaur-2os", Generations = [
                G("HTK (2015–)", "humbaur-htk-2015", 2015, null) ]},
            new Model { BrandId = B("Humbaur"), Name = "Laweta", Slug = "humbaur-laweta", Generations = [
                G("HBT (2015–)", "humbaur-hbt-2015", 2015, null) ]},
        ]);

        // ─── NIEWIADÓW (przyczepy) ────────────────────────────────────────────────
        if (NeedsSeeding("Niewiadów")) models.AddRange([
            new Model { BrandId = B("Niewiadów"), Name = "N126", Slug = "niewiadow-n126", Generations = [
                G("N126 (2010–)", "niewiadow-n126-2010", 2010, null) ]},
            new Model { BrandId = B("Niewiadów"), Name = "N750", Slug = "niewiadow-n750", Generations = [
                G("N750 (2010–)", "niewiadow-n750-2010", 2010, null) ]},
        ]);

        // ─── SCHMITZ CARGOBULL (przyczepy) ───────────────────────────────────────
        if (NeedsSeeding("Schmitz Cargobull")) models.AddRange([
            new Model { BrandId = B("Schmitz Cargobull"), Name = "Naczepa chłodnicza", Slug = "schmitz-chl", Generations = [
                G("SKO (2015–)", "schmitz-sko-2015", 2015, null) ]},
            new Model { BrandId = B("Schmitz Cargobull"), Name = "Naczepa plandeka", Slug = "schmitz-plandeka", Generations = [
                G("SCS (2015–)", "schmitz-scs-2015", 2015, null) ]},
            new Model { BrandId = B("Schmitz Cargobull"), Name = "Naczepa skrzyniowa", Slug = "schmitz-skrzyniowa", Generations = [
                G("SCB (2015–)", "schmitz-scb-2015", 2015, null) ]},
        ]);

        // ─── KRONE (przyczepy) ────────────────────────────────────────────────────
        if (NeedsSeeding("Krone")) models.AddRange([
            new Model { BrandId = B("Krone"), Name = "Profi Liner", Slug = "krone-profi-liner", Generations = [
                G("SD (2015–)", "krone-sd-2015", 2015, null) ]},
            new Model { BrandId = B("Krone"), Name = "Cool Liner", Slug = "krone-cool-liner", Generations = [
                G("SDR (2015–)", "krone-sdr-2015", 2015, null) ]},
        ]);

        // ─── WIELTON (przyczepy) ──────────────────────────────────────────────────
        if (NeedsSeeding("Wielton")) models.AddRange([
            new Model { BrandId = B("Wielton"), Name = "Platforma", Slug = "wielton-platforma", Generations = [
                G("NS-3 (2015–)", "wielton-ns3-2015", 2015, null) ]},
            new Model { BrandId = B("Wielton"), Name = "Plandeka", Slug = "wielton-plandeka", Generations = [
                G("NW-3 (2015–)", "wielton-nw3-2015", 2015, null) ]},
        ]);

        // ─── FLIEGL (przyczepy) ───────────────────────────────────────────────────
        if (NeedsSeeding("Fliegl")) models.AddRange([
            new Model { BrandId = B("Fliegl"), Name = "Przyczepa rolnicza", Slug = "fliegl-rolnicza", Generations = [
                G("DK (2015–)", "fliegl-dk-2015", 2015, null) ]},
            new Model { BrandId = B("Fliegl"), Name = "Naczepa", Slug = "fliegl-naczepa", Generations = [
                G("SDS (2015–)", "fliegl-sds-2015", 2015, null) ]},
        ]);

        // ─── KOGEL (przyczepy) ────────────────────────────────────────────────────
        if (NeedsSeeding("Kogel")) models.AddRange([
            new Model { BrandId = B("Kogel"), Name = "Cargo", Slug = "kogel-cargo", Generations = [
                G("S 24 (2015–)", "kogel-s24-2015", 2015, null) ]},
            new Model { BrandId = B("Kogel"), Name = "Overland", Slug = "kogel-overland", Generations = [
                G("PN 24 (2015–)", "kogel-pn24-2015", 2015, null) ]},
        ]);

        // ─── SCHWARZMÜLLER (przyczepy) ────────────────────────────────────────────
        if (NeedsSeeding("Schwarzmüller")) models.AddRange([
            new Model { BrandId = B("Schwarzmüller"), Name = "Naczepa skrzyniowa", Slug = "schwarzmuller-skrzyniowa", Generations = [
                G("RH200 (2015–)", "schwarzmuller-rh200-2015", 2015, null) ]},
        ]);

        // ─── MEILLER (przyczepy) ──────────────────────────────────────────────────
        if (NeedsSeeding("Meiller")) models.AddRange([
            new Model { BrandId = B("Meiller"), Name = "Wywrotka", Slug = "meiller-wywrotka", Generations = [
                G("D3K (2015–)", "meiller-d3k-2015", 2015, null) ]},
        ]);

        // ─── NOOTEBOOM (przyczepy) ────────────────────────────────────────────────
        if (NeedsSeeding("Nooteboom")) models.AddRange([
            new Model { BrandId = B("Nooteboom"), Name = "Megamax", Slug = "nooteboom-megamax", Generations = [
                G("OSD-73-04V (2015–)", "nooteboom-osd73-2015", 2015, null) ]},
            new Model { BrandId = B("Nooteboom"), Name = "Semi Lowloader", Slug = "nooteboom-lowloader", Generations = [
                G("OSDS (2015–)", "nooteboom-osds-2015", 2015, null) ]},
        ]);

        // ─── JOHN DEERE ───────────────────────────────────────────────────────────
        if (NeedsSeeding("John Deere")) models.AddRange([
            new Model { BrandId = B("John Deere"), Name = "Seria 6", Slug = "jd-seria-6", Generations = [
                G("6M/6R (2014–)", "jd-seria6-2014", 2014, null,
                    E("6110R 110 KM", 110, 81, 4530, die, null, null, null), E("6130R 130 KM", 130, 96, 4530, die, null, null, null),
                    E("6155R 155 KM", 155, 114, 6800, die, null, null, null), E("6175R 175 KM", 175, 129, 6800, die, null, null, null)) ]},
            new Model { BrandId = B("John Deere"), Name = "Seria 7", Slug = "jd-seria-7", Generations = [
                G("7R (2014–)", "jd-seria7-2014", 2014, null,
                    E("7230R 230 KM", 230, 169, 9000, die, null, null, null), E("7260R 260 KM", 260, 191, 9000, die, null, null, null),
                    E("7310R 310 KM", 310, 228, 9000, die, null, null, null)) ]},
        ]);

        // ─── FENDT ────────────────────────────────────────────────────────────────
        if (NeedsSeeding("Fendt")) models.AddRange([
            new Model { BrandId = B("Fendt"), Name = "Vario 700", Slug = "fendt-vario-700", Generations = [
                G("Gen6 (2014–)", "fendt-700-gen6", 2014, null,
                    E("718 Vario 177 KM", 177, 130, 6057, die, null, null, null), E("720 Vario 200 KM", 200, 147, 6057, die, null, null, null),
                    E("724 Vario 240 KM", 240, 177, 6057, die, null, null, null), E("728 Vario 281 KM", 281, 207, 6057, die, null, null, null)) ]},
            new Model { BrandId = B("Fendt"), Name = "Vario 900", Slug = "fendt-vario-900", Generations = [
                G("Gen6 (2014–)", "fendt-900-gen6", 2014, null,
                    E("927 Vario 270 KM", 270, 199, 8400, die, null, null, null), E("930 Vario 300 KM", 300, 221, 8400, die, null, null, null),
                    E("936 Vario 360 KM", 360, 265, 8400, die, null, null, null), E("939 Vario 390 KM", 390, 287, 8400, die, null, null, null)) ]},
        ]);

        // ─── NEW HOLLAND ──────────────────────────────────────────────────────────
        if (NeedsSeeding("New Holland")) models.AddRange([
            new Model { BrandId = B("New Holland"), Name = "T6", Slug = "nh-t6", Generations = [
                G("T6.xxx (2014–)", "nh-t6-2014", 2014, null,
                    E("T6.140 140 KM", 140, 103, 4485, die, null, null, null), E("T6.160 160 KM", 160, 118, 4485, die, null, null, null),
                    E("T6.180 180 KM", 180, 132, 6728, die, null, null, null)) ]},
            new Model { BrandId = B("New Holland"), Name = "T7", Slug = "nh-t7", Generations = [
                G("T7.xxx (2014–)", "nh-t7-2014", 2014, null,
                    E("T7.175 175 KM", 175, 129, 6728, die, null, null, null), E("T7.210 210 KM", 210, 154, 6728, die, null, null, null),
                    E("T7.260 260 KM", 260, 191, 8728, die, null, null, null)) ]},
        ]);

        // ─── DAF ──────────────────────────────────────────────────────────────────
        if (B("DAF") > 0) models.AddRange([
            new Model { BrandId = B("DAF"), Name = "XF", Slug = "daf-xf", Generations = [
                G("XF105 (2006–2017)", "daf-xf105-2006", 2006, 2017,
                    E("MX-340 340 KM", 340, 250, 12902, die, 30.0m, 25.0m, 28.0m), E("MX-375 375 KM", 375, 276, 12902, die, 30.0m, 25.0m, 28.0m),
                    E("MX-410 410 KM", 410, 302, 12902, die, 30.0m, 25.0m, 28.0m), E("MX-460 460 KM", 460, 338, 12902, die, 30.0m, 25.0m, 28.0m)),
                G("XF NG (2017–)", "daf-xf-ng", 2017, null,
                    E("MX-375 375 KM", 375, 276, 12902, die, 30.0m, 25.0m, 28.0m), E("MX-430 430 KM", 430, 316, 12902, die, 30.0m, 25.0m, 28.0m),
                    E("MX-480 480 KM", 480, 353, 12902, die, 30.0m, 25.0m, 28.0m), E("MX-530 530 KM", 530, 390, 12902, die, 30.0m, 25.0m, 28.0m)) ]},
            new Model { BrandId = B("DAF"), Name = "CF", Slug = "daf-cf", Generations = [
                G("CF85 (2001–2017)", "daf-cf85-2001", 2001, 2017,
                    E("MX-300 300 KM", 300, 221, 12902, die, 30.0m, 25.0m, 28.0m), E("MX-340 340 KM", 340, 250, 12902, die, 30.0m, 25.0m, 28.0m),
                    E("MX-375 375 KM", 375, 276, 12902, die, 30.0m, 25.0m, 28.0m), E("MX-410 410 KM", 410, 302, 12902, die, 30.0m, 25.0m, 28.0m)),
                G("CF NG (2017–)", "daf-cf-ng", 2017, null,
                    E("MX-310 310 KM", 310, 228, 12902, die, 30.0m, 25.0m, 28.0m), E("MX-350 350 KM", 350, 257, 12902, die, 30.0m, 25.0m, 28.0m),
                    E("MX-395 395 KM", 395, 291, 12902, die, 30.0m, 25.0m, 28.0m), E("MX-460 460 KM", 460, 338, 12902, die, 30.0m, 25.0m, 28.0m)) ]},
            new Model { BrandId = B("DAF"), Name = "LF", Slug = "daf-lf", Generations = [
                G("LF (2006–)", "daf-lf-2006", 2006, null,
                    E("PX-5 150 KM", 150, 110, 4486, die, 12.0m, 9.0m, 10.5m), E("PX-5 180 KM", 180, 132, 4486, die, 12.0m, 9.0m, 10.5m),
                    E("PX-7 210 KM", 210, 154, 6728, die, 18.0m, 14.0m, 16.0m), E("PX-7 220 KM", 220, 162, 6728, die, 18.0m, 14.0m, 16.0m)) ]},
        ]);

        // ─── IVECO ────────────────────────────────────────────────────────────────
        if (B("Iveco") > 0) models.AddRange([
            new Model { BrandId = B("Iveco"), Name = "Stralis", Slug = "iveco-stralis", Generations = [
                G("Stralis I (2002–2012)", "iveco-stralis-i", 2002, 2012,
                    E("Cursor 10 400 KM", 400, 294, 10308, die, 30.0m, 25.0m, 28.0m), E("Cursor 10 430 KM", 430, 316, 10308, die, 30.0m, 25.0m, 28.0m),
                    E("Cursor 13 480 KM", 480, 353, 12882, die, 30.0m, 25.0m, 28.0m), E("Cursor 13 500 KM", 500, 368, 12882, die, 30.0m, 25.0m, 28.0m)),
                G("Stralis Hi-Way (2012–)", "iveco-stralis-hiway", 2012, null,
                    E("Cursor 10 420 KM", 420, 309, 10308, die, 30.0m, 25.0m, 28.0m), E("Cursor 11 460 KM", 460, 338, 10874, die, 30.0m, 25.0m, 28.0m),
                    E("Cursor 13 500 KM", 500, 368, 12882, die, 30.0m, 25.0m, 28.0m), E("Cursor 13 560 KM", 560, 412, 12882, die, 30.0m, 25.0m, 28.0m)) ]},
            new Model { BrandId = B("Iveco"), Name = "Eurocargo", Slug = "iveco-eurocargo", Generations = [
                G("Eurocargo (2008–)", "iveco-eurocargo-2008", 2008, null,
                    E("F4AE 150 KM", 150, 110, 3920, die, 18.0m, 14.0m, 16.0m), E("F4AE 180 KM", 180, 132, 3920, die, 18.0m, 14.0m, 16.0m),
                    E("F4AE 220 KM", 220, 162, 5880, die, 18.0m, 14.0m, 16.0m), E("F4AE 250 KM", 250, 184, 5880, die, 18.0m, 14.0m, 16.0m),
                    E("F4AE 280 KM", 280, 206, 5880, die, 18.0m, 14.0m, 16.0m)) ]},
            new Model { BrandId = B("Iveco"), Name = "Daily", Slug = "iveco-daily", Generations = [
                G("VI (2014–)", "iveco-daily-vi", 2014, null,
                    E("F1A 120 KM", 120, 88, 2287, die, 12.0m, 9.0m, 10.5m), E("F1A 140 KM", 140, 103, 2287, die, 12.0m, 9.0m, 10.5m),
                    E("F1C 160 KM", 160, 118, 2998, die, 12.0m, 9.0m, 10.5m), E("F1C 180 KM", 180, 132, 2998, die, 12.0m, 9.0m, 10.5m),
                    E("F1C 210 KM", 210, 154, 2998, die, 12.0m, 9.0m, 10.5m)) ]},
        ]);

        // ─── RENAULT TRUCKS ───────────────────────────────────────────────────────
        if (B("Renault Trucks") > 0) models.AddRange([
            new Model { BrandId = B("Renault Trucks"), Name = "T-series", Slug = "renault-trucks-t", Generations = [
                G("T (2013–)", "renault-trucks-t-2013", 2013, null,
                    E("DTI11 380 KM", 380, 279, 10837, die, 30.0m, 25.0m, 28.0m), E("DTI11 430 KM", 430, 316, 10837, die, 30.0m, 25.0m, 28.0m),
                    E("DTI13 480 KM", 480, 353, 12777, die, 30.0m, 25.0m, 28.0m), E("DTI13 520 KM", 520, 382, 12777, die, 30.0m, 25.0m, 28.0m)) ]},
            new Model { BrandId = B("Renault Trucks"), Name = "C-series", Slug = "renault-trucks-c", Generations = [
                G("C (2013–)", "renault-trucks-c-2013", 2013, null,
                    E("DTI8 330 KM", 330, 243, 7696, die, 18.0m, 14.0m, 16.0m), E("DTI8 370 KM", 370, 272, 7696, die, 18.0m, 14.0m, 16.0m),
                    E("DTI11 410 KM", 410, 302, 10837, die, 18.0m, 14.0m, 16.0m), E("DTI11 460 KM", 460, 338, 10837, die, 18.0m, 14.0m, 16.0m)) ]},
            new Model { BrandId = B("Renault Trucks"), Name = "D-series", Slug = "renault-trucks-d", Generations = [
                G("D (2013–)", "renault-trucks-d-2013", 2013, null,
                    E("DTI5 210 KM", 210, 154, 4769, die, 12.0m, 9.0m, 10.5m), E("DTI8 250 KM", 250, 184, 7696, die, 12.0m, 9.0m, 10.5m),
                    E("DTI8 280 KM", 280, 206, 7696, die, 12.0m, 9.0m, 10.5m), E("DTI8 330 KM", 330, 243, 7696, die, 12.0m, 9.0m, 10.5m)) ]},
        ]);

        // ─── BMW (motorcycles) ────────────────────────────────────────────────────
        if (B("BMW") > 0) models.AddRange([
            new Model { BrandId = B("BMW"), Name = "R 1250 GS", Slug = "bmw-r1250gs", Generations = [
                G("2018–", "bmw-r1250gs-2018", 2018, null,
                    E("1254cc Boxer 136 KM", 136, 100, 1254, ben, 7.5m, 6.0m, 6.5m)) ]},
            new Model { BrandId = B("BMW"), Name = "F 900 R", Slug = "bmw-f900r", Generations = [
                G("2020–", "bmw-f900r-2020", 2020, null,
                    E("895cc parallel twin 105 KM", 105, 77, 895, ben, 8.0m, 6.0m, 7.0m)) ]},
            new Model { BrandId = B("BMW"), Name = "S 1000 RR", Slug = "bmw-s1000rr", Generations = [
                G("2009–2018", "bmw-s1000rr-2009", 2009, 2018,
                    E("999cc inline-4 193 KM", 193, 142, 999, ben, 8.0m, 6.0m, 7.0m)),
                G("2019–", "bmw-s1000rr-2019", 2019, null,
                    E("999cc inline-4 210 KM", 210, 154, 999, ben, 8.0m, 6.0m, 7.0m)) ]},
            new Model { BrandId = B("BMW"), Name = "R nineT", Slug = "bmw-r-ninet", Generations = [
                G("2014–", "bmw-r-ninet-2014", 2014, null,
                    E("1170cc Boxer 109 KM", 109, 80, 1170, ben, 7.5m, 6.0m, 6.5m)) ]},
        ]);

        // ─── HONDA (motorcycles) ──────────────────────────────────────────────────
        if (B("Honda") > 0) models.AddRange([
            new Model { BrandId = B("Honda"), Name = "CB500F", Slug = "honda-cb500f", Generations = [
                G("2013–", "honda-cb500f-2013", 2013, null,
                    E("471cc parallel twin 47 KM", 47, 35, 471, ben, 4.5m, 3.5m, 4.0m)) ]},
            new Model { BrandId = B("Honda"), Name = "Africa Twin CRF1100L", Slug = "honda-africa-twin-crf1100l", Generations = [
                G("2020–", "honda-africa-twin-2020", 2020, null,
                    E("1084cc parallel twin 101 KM", 101, 74, 1084, ben, 7.5m, 6.0m, 6.5m)) ]},
            new Model { BrandId = B("Honda"), Name = "CBR1000RR Fireblade", Slug = "honda-cbr1000rr-fireblade", Generations = [
                G("2017–", "honda-cbr1000rr-2017", 2017, null,
                    E("999cc inline-4 214 KM", 214, 157, 999, ben, 8.0m, 6.0m, 7.0m)) ]},
            new Model { BrandId = B("Honda"), Name = "Gold Wing GL1800", Slug = "honda-gold-wing-gl1800", Generations = [
                G("2018–", "honda-gold-wing-2018", 2018, null,
                    E("1833cc flat-6 126 KM", 126, 93, 1833, ben, 7.5m, 6.0m, 6.5m)) ]},
            new Model { BrandId = B("Honda"), Name = "CB650R", Slug = "honda-cb650r", Generations = [
                G("2019–", "honda-cb650r-2019", 2019, null,
                    E("649cc inline-4 95 KM", 95, 70, 649, ben, 6.0m, 4.5m, 5.0m)) ]},
        ]);

        // ─── SUZUKI (motorcycles) ─────────────────────────────────────────────────
        if (B("Suzuki") > 0) models.AddRange([
            new Model { BrandId = B("Suzuki"), Name = "GSX-R1000", Slug = "suzuki-gsx-r1000", Generations = [
                G("2017–", "suzuki-gsx-r1000-2017", 2017, null,
                    E("999cc inline-4 202 KM", 202, 149, 999, ben, 8.0m, 6.0m, 7.0m)) ]},
            new Model { BrandId = B("Suzuki"), Name = "V-Strom 650", Slug = "suzuki-v-strom-650", Generations = [
                G("2017–", "suzuki-v-strom-650-2017", 2017, null,
                    E("645cc V-twin 71 KM", 71, 52, 645, ben, 6.0m, 4.5m, 5.0m)) ]},
            new Model { BrandId = B("Suzuki"), Name = "GSX-S1000", Slug = "suzuki-gsx-s1000", Generations = [
                G("2021–", "suzuki-gsx-s1000-2021", 2021, null,
                    E("999cc inline-4 152 KM", 152, 112, 999, ben, 8.0m, 6.0m, 7.0m)) ]},
            new Model { BrandId = B("Suzuki"), Name = "Hayabusa", Slug = "suzuki-hayabusa", Generations = [
                G("2021–", "suzuki-hayabusa-2021", 2021, null,
                    E("1340cc inline-4 190 KM", 190, 140, 1340, ben, 7.5m, 6.0m, 6.5m)) ]},
        ]);

        // ─── APRILIA ──────────────────────────────────────────────────────────────
        if (B("Aprilia") > 0) models.AddRange([
            new Model { BrandId = B("Aprilia"), Name = "RSV4", Slug = "aprilia-rsv4", Generations = [
                G("2021–", "aprilia-rsv4-2021", 2021, null,
                    E("1099cc V4 217 KM", 217, 160, 1099, ben, 8.0m, 6.0m, 7.0m)) ]},
            new Model { BrandId = B("Aprilia"), Name = "Tuono V4", Slug = "aprilia-tuono-v4", Generations = [
                G("2021–", "aprilia-tuono-v4-2021", 2021, null,
                    E("1099cc V4 175 KM", 175, 129, 1099, ben, 8.0m, 6.0m, 7.0m)) ]},
            new Model { BrandId = B("Aprilia"), Name = "RS 660", Slug = "aprilia-rs-660", Generations = [
                G("2020–", "aprilia-rs-660-2020", 2020, null,
                    E("659cc parallel twin 100 KM", 100, 74, 659, ben, 6.0m, 4.5m, 5.0m)) ]},
        ]);

        // ─── ROYAL ENFIELD ────────────────────────────────────────────────────────
        if (B("Royal Enfield") > 0) models.AddRange([
            new Model { BrandId = B("Royal Enfield"), Name = "Classic 350", Slug = "royal-enfield-classic-350", Generations = [
                G("2021–", "royal-enfield-classic-350-2021", 2021, null,
                    E("349cc single-cylinder 20 KM", 20, 15, 349, ben, 4.5m, 3.5m, 4.0m)) ]},
            new Model { BrandId = B("Royal Enfield"), Name = "Himalayan 450", Slug = "royal-enfield-himalayan-450", Generations = [
                G("2024–", "royal-enfield-himalayan-450-2024", 2024, null,
                    E("452cc single-cylinder 40 KM", 40, 29, 452, ben, 4.5m, 3.5m, 4.0m)) ]},
            new Model { BrandId = B("Royal Enfield"), Name = "Meteor 350", Slug = "royal-enfield-meteor-350", Generations = [
                G("2020–", "royal-enfield-meteor-350-2020", 2020, null,
                    E("349cc single-cylinder 20 KM", 20, 15, 349, ben, 4.5m, 3.5m, 4.0m)) ]},
        ]);

        // ─── INDIAN ───────────────────────────────────────────────────────────────
        if (B("Indian") > 0) models.AddRange([
            new Model { BrandId = B("Indian"), Name = "Scout", Slug = "indian-scout", Generations = [
                G("2015–", "indian-scout-2015", 2015, null,
                    E("1133cc V-twin 100 KM", 100, 74, 1133, ben, 7.5m, 6.0m, 6.5m)) ]},
            new Model { BrandId = B("Indian"), Name = "Chief", Slug = "indian-chief", Generations = [
                G("2021–", "indian-chief-2021", 2021, null,
                    E("1890cc Thunderstroke 116 V-twin 93 KM", 93, 68, 1890, ben, 7.5m, 6.0m, 6.5m)) ]},
            new Model { BrandId = B("Indian"), Name = "Challenger", Slug = "indian-challenger", Generations = [
                G("2020–", "indian-challenger-2020", 2020, null,
                    E("1768cc PowerPlus V-twin 122 KM", 122, 90, 1768, ben, 7.5m, 6.0m, 6.5m)) ]},
        ]);

        // ─── MV AGUSTA ────────────────────────────────────────────────────────────
        if (B("MV Agusta") > 0) models.AddRange([
            new Model { BrandId = B("MV Agusta"), Name = "Brutale 800", Slug = "mv-agusta-brutale-800", Generations = [
                G("2012–", "mv-agusta-brutale-800-2012", 2012, null,
                    E("798cc inline-3 140 KM", 140, 103, 798, ben, 8.0m, 6.0m, 7.0m)) ]},
            new Model { BrandId = B("MV Agusta"), Name = "Turismo Veloce 800", Slug = "mv-agusta-turismo-veloce-800", Generations = [
                G("2015–", "mv-agusta-turismo-veloce-2015", 2015, null,
                    E("798cc inline-3 110 KM", 110, 81, 798, ben, 6.5m, 5.0m, 5.8m)) ]},
        ]);

        // ─── HUSQVARNA ────────────────────────────────────────────────────────────
        if (B("Husqvarna") > 0) models.AddRange([
            new Model { BrandId = B("Husqvarna"), Name = "Vitpilen 401", Slug = "husqvarna-vitpilen-401", Generations = [
                G("2018–", "husqvarna-vitpilen-401-2018", 2018, null,
                    E("373cc single-cylinder 44 KM", 44, 32, 373, ben, 4.5m, 3.5m, 4.0m)) ]},
            new Model { BrandId = B("Husqvarna"), Name = "Svartpilen 401", Slug = "husqvarna-svartpilen-401", Generations = [
                G("2018–", "husqvarna-svartpilen-401-2018", 2018, null,
                    E("373cc single-cylinder 44 KM", 44, 32, 373, ben, 4.5m, 3.5m, 4.0m)) ]},
            new Model { BrandId = B("Husqvarna"), Name = "Norden 901", Slug = "husqvarna-norden-901", Generations = [
                G("2022–", "husqvarna-norden-901-2022", 2022, null,
                    E("889cc parallel twin 105 KM", 105, 77, 889, ben, 6.5m, 5.0m, 5.8m)) ]},
        ]);

        // ─── CASE IH ──────────────────────────────────────────────────────────────
        if (B("Case IH") > 0) models.AddRange([
            new Model { BrandId = B("Case IH"), Name = "Puma", Slug = "case-ih-puma", Generations = [
                G("Puma (2014–)", "case-ih-puma-2014", 2014, null,
                    E("FPT F5H 150 KM", 150, 110, 4485, die, null, null, null), E("FPT F5H 185 KM", 185, 136, 4485, die, null, null, null),
                    E("FPT F5H 210 KM", 210, 154, 4485, die, null, null, null), E("FPT Cursor 9 240 KM", 240, 177, 8728, die, null, null, null)) ]},
            new Model { BrandId = B("Case IH"), Name = "Maxxum", Slug = "case-ih-maxxum", Generations = [
                G("Maxxum (2014–)", "case-ih-maxxum-2014", 2014, null,
                    E("FPT F5H 110 KM", 110, 81, 4485, die, null, null, null), E("FPT F5H 125 KM", 125, 92, 4485, die, null, null, null),
                    E("FPT F5H 145 KM", 145, 107, 4485, die, null, null, null)) ]},
            new Model { BrandId = B("Case IH"), Name = "Optum", Slug = "case-ih-optum", Generations = [
                G("Optum (2016–)", "case-ih-optum-2016", 2016, null,
                    E("FPT Cursor 9 270 KM", 270, 199, 8728, die, null, null, null), E("FPT Cursor 9 300 KM", 300, 221, 8728, die, null, null, null),
                    E("FPT Cursor 9 340 KM", 340, 250, 8728, die, null, null, null)) ]},
        ]);

        // ─── CLAAS ────────────────────────────────────────────────────────────────
        if (B("Claas") > 0) models.AddRange([
            new Model { BrandId = B("Claas"), Name = "Axion 800", Slug = "claas-axion-800", Generations = [
                G("Axion 800 (2015–)", "claas-axion-800-2015", 2015, null,
                    E("FPT Cursor 9 205 KM", 205, 151, 8728, die, null, null, null), E("FPT Cursor 9 245 KM", 245, 180, 8728, die, null, null, null),
                    E("FPT Cursor 9 270 KM", 270, 199, 8728, die, null, null, null), E("FPT Cursor 9 295 KM", 295, 217, 8728, die, null, null, null)) ]},
            new Model { BrandId = B("Claas"), Name = "Arion 600", Slug = "claas-arion-600", Generations = [
                G("Arion 600 (2015–)", "claas-arion-600-2015", 2015, null,
                    E("FPT F5H 130 KM", 130, 96, 4485, die, null, null, null), E("FPT F5H 155 KM", 155, 114, 4485, die, null, null, null),
                    E("FPT F5H 185 KM", 185, 136, 4485, die, null, null, null)) ]},
            new Model { BrandId = B("Claas"), Name = "Lexion 8000", Slug = "claas-lexion-8000", Generations = [
                G("Lexion 8000 (2019–)", "claas-lexion-8000-2019", 2019, null,
                    E("Mercedes-Benz OM473 476 KM", 476, 350, 15600, die, null, null, null),
                    E("Mercedes-Benz OM473 598 KM", 598, 440, 15600, die, null, null, null),
                    E("Mercedes-Benz OM473 627 KM", 627, 461, 15600, die, null, null, null)) ]},
            new Model { BrandId = B("Claas"), Name = "Jaguar 900", Slug = "claas-jaguar-900", Generations = [
                G("Jaguar 900 (2016–)", "claas-jaguar-900-2016", 2016, null,
                    E("Mercedes-Benz OM471 624 KM", 624, 459, 12799, die, null, null, null),
                    E("Mercedes-Benz OM473 680 KM", 680, 500, 15600, die, null, null, null),
                    E("Mercedes-Benz OM473 730 KM", 730, 537, 15600, die, null, null, null)) ]},
        ]);

        // ─── MASSEY FERGUSON ──────────────────────────────────────────────────────
        if (B("Massey Ferguson") > 0) models.AddRange([
            new Model { BrandId = B("Massey Ferguson"), Name = "5700S", Slug = "massey-ferguson-5700s", Generations = [
                G("5700S (2017–)", "massey-ferguson-5700s-2017", 2017, null,
                    E("AGCO Power 4.4L 105 KM", 105, 77, 4400, die, null, null, null), E("AGCO Power 4.4L 120 KM", 120, 88, 4400, die, null, null, null),
                    E("AGCO Power 4.4L 140 KM", 140, 103, 4400, die, null, null, null)) ]},
            new Model { BrandId = B("Massey Ferguson"), Name = "6700S", Slug = "massey-ferguson-6700s", Generations = [
                G("6700S (2016–)", "massey-ferguson-6700s-2016", 2016, null,
                    E("AGCO Power 6.6L 155 KM", 155, 114, 6600, die, null, null, null), E("AGCO Power 6.6L 175 KM", 175, 129, 6600, die, null, null, null),
                    E("AGCO Power 6.6L 205 KM", 205, 151, 6600, die, null, null, null)) ]},
            new Model { BrandId = B("Massey Ferguson"), Name = "7700S", Slug = "massey-ferguson-7700s", Generations = [
                G("7700S (2016–)", "massey-ferguson-7700s-2016", 2016, null,
                    E("AGCO Power 6.6L 215 KM", 215, 158, 6600, die, null, null, null), E("AGCO Power 6.6L 240 KM", 240, 177, 6600, die, null, null, null),
                    E("AGCO Power 6.6L 270 KM", 270, 199, 6600, die, null, null, null)) ]},
        ]);

        // ─── ZETOR ────────────────────────────────────────────────────────────────
        if (B("Zetor") > 0) models.AddRange([
            new Model { BrandId = B("Zetor"), Name = "Forterra", Slug = "zetor-forterra", Generations = [
                G("Forterra (2010–)", "zetor-forterra-2010", 2010, null,
                    E("Z 1006 100 KM", 100, 74, 6211, die, null, null, null), E("Z 1006 115 KM", 115, 85, 6211, die, null, null, null),
                    E("Z 1006 135 KM", 135, 99, 6211, die, null, null, null), E("Z 1006 150 KM", 150, 110, 6211, die, null, null, null)) ]},
            new Model { BrandId = B("Zetor"), Name = "Proxima", Slug = "zetor-proxima", Generations = [
                G("Proxima (2010–)", "zetor-proxima-2010", 2010, null,
                    E("Z 1006 75 KM", 75, 55, 6211, die, null, null, null), E("Z 1006 90 KM", 90, 66, 6211, die, null, null, null),
                    E("Z 1006 105 KM", 105, 77, 6211, die, null, null, null)) ]},
        ]);

        // ─── KUBOTA ───────────────────────────────────────────────────────────────
        if (B("Kubota") > 0) models.AddRange([
            new Model { BrandId = B("Kubota"), Name = "M7", Slug = "kubota-m7", Generations = [
                G("M7 (2015–)", "kubota-m7-2015", 2015, null,
                    E("V6108 121 KM", 121, 89, 6100, die, null, null, null), E("V6108 145 KM", 145, 107, 6100, die, null, null, null),
                    E("V6108 175 KM", 175, 129, 6100, die, null, null, null)) ]},
            new Model { BrandId = B("Kubota"), Name = "M5", Slug = "kubota-m5", Generations = [
                G("M5 (2016–)", "kubota-m5-2016", 2016, null,
                    E("V3800 95 KM", 95, 70, 3769, die, null, null, null), E("V3800 105 KM", 105, 77, 3769, die, null, null, null),
                    E("V3800 115 KM", 115, 85, 3769, die, null, null, null)) ]},
            new Model { BrandId = B("Kubota"), Name = "L-Series", Slug = "kubota-l-series", Generations = [
                G("L-Series (2010–)", "kubota-l-series-2010", 2010, null,
                    E("D1305 24 KM", 24, 18, 1261, die, null, null, null), E("D1803 37 KM", 37, 27, 1826, die, null, null, null),
                    E("V2403 52 KM", 52, 38, 2434, die, null, null, null), E("V3307 70 KM", 70, 51, 3318, die, null, null, null)) ]},
        ]);

        // ─── CATERPILLAR ──────────────────────────────────────────────────────────
        if (B("Caterpillar") > 0) models.AddRange([
            new Model { BrandId = B("Caterpillar"), Name = "320", Slug = "caterpillar-320", Generations = [
                G("320 (2019–)", "caterpillar-320-2019", 2019, null,
                    E("Cat C4.4 ACERT 121 KM", 121, 89, 4400, die, null, null, null)) ]},
            new Model { BrandId = B("Caterpillar"), Name = "336", Slug = "caterpillar-336", Generations = [
                G("336 (2019–)", "caterpillar-336-2019", 2019, null,
                    E("Cat C9.3B 265 KM", 265, 195, 9300, die, null, null, null)) ]},
            new Model { BrandId = B("Caterpillar"), Name = "966", Slug = "caterpillar-966", Generations = [
                G("966 (2017–)", "caterpillar-966-2017", 2017, null,
                    E("Cat C9.3B 263 KM", 263, 193, 9300, die, null, null, null)) ]},
            new Model { BrandId = B("Caterpillar"), Name = "D6", Slug = "caterpillar-d6", Generations = [
                G("D6 (2017–)", "caterpillar-d6-2017", 2017, null,
                    E("Cat C9.3B 215 KM", 215, 158, 9300, die, null, null, null)) ]},
        ]);

        // ─── JCB ──────────────────────────────────────────────────────────────────
        if (B("JCB") > 0) models.AddRange([
            new Model { BrandId = B("JCB"), Name = "3CX", Slug = "jcb-3cx", Generations = [
                G("3CX (2008–)", "jcb-3cx-2008", 2008, null,
                    E("JCB EcoMAX 109 KM", 109, 80, 4400, die, null, null, null)) ]},
            new Model { BrandId = B("JCB"), Name = "4CX", Slug = "jcb-4cx", Generations = [
                G("4CX (2008–)", "jcb-4cx-2008", 2008, null,
                    E("JCB EcoMAX 109 KM", 109, 80, 4400, die, null, null, null)) ]},
            new Model { BrandId = B("JCB"), Name = "JS220", Slug = "jcb-js220", Generations = [
                G("JS220 (2015–)", "jcb-js220-2015", 2015, null,
                    E("JCB EcoMAX 160 KM", 160, 118, 4400, die, null, null, null)) ]},
            new Model { BrandId = B("JCB"), Name = "540-180", Slug = "jcb-540-180", Generations = [
                G("540-180 (2016–)", "jcb-540-180-2016", 2016, null,
                    E("JCB EcoMAX 109 KM", 109, 80, 4400, die, null, null, null)) ]},
        ]);

        // ─── KOMATSU ──────────────────────────────────────────────────────────────
        if (B("Komatsu") > 0) models.AddRange([
            new Model { BrandId = B("Komatsu"), Name = "PC200", Slug = "komatsu-pc200", Generations = [
                G("PC200 (2012–)", "komatsu-pc200-2012", 2012, null,
                    E("SAA6D107E 112 KM", 112, 82, 6690, die, null, null, null)) ]},
            new Model { BrandId = B("Komatsu"), Name = "PC360", Slug = "komatsu-pc360", Generations = [
                G("PC360 (2012–)", "komatsu-pc360-2012", 2012, null,
                    E("SAA6D114E 258 KM", 258, 190, 8270, die, null, null, null)) ]},
            new Model { BrandId = B("Komatsu"), Name = "WA320", Slug = "komatsu-wa320", Generations = [
                G("WA320 (2013–)", "komatsu-wa320-2013", 2013, null,
                    E("SAA6D107E 150 KM", 150, 110, 6690, die, null, null, null)) ]},
        ]);

        // ─── LIEBHERR ─────────────────────────────────────────────────────────────
        if (B("Liebherr") > 0) models.AddRange([
            new Model { BrandId = B("Liebherr"), Name = "R 924", Slug = "liebherr-r-924", Generations = [
                G("R 924 (2018–)", "liebherr-r-924-2018", 2018, null,
                    E("Liebherr D934 156 KM", 156, 115, 6700, die, null, null, null)) ]},
            new Model { BrandId = B("Liebherr"), Name = "R 934", Slug = "liebherr-r-934", Generations = [
                G("R 934 (2018–)", "liebherr-r-934-2018", 2018, null,
                    E("Liebherr D946 215 KM", 215, 158, 9900, die, null, null, null)) ]},
            new Model { BrandId = B("Liebherr"), Name = "LTM 1030", Slug = "liebherr-ltm-1030", Generations = [
                G("LTM 1030 (2010–)", "liebherr-ltm-1030-2010", 2010, null,
                    E("Liebherr D924 174 KM", 174, 128, 6700, die, null, null, null)) ]},
        ]);

        // ─── BOBCAT ───────────────────────────────────────────────────────────────
        if (B("Bobcat") > 0) models.AddRange([
            new Model { BrandId = B("Bobcat"), Name = "E50", Slug = "bobcat-e50", Generations = [
                G("E50 (2014–)", "bobcat-e50-2014", 2014, null,
                    E("Kubota V2607 41 KM", 41, 30, 2615, die, null, null, null)) ]},
            new Model { BrandId = B("Bobcat"), Name = "T650", Slug = "bobcat-t650", Generations = [
                G("T650 (2012–)", "bobcat-t650-2012", 2012, null,
                    E("Bobcat/Doosan D24 74 KM", 74, 54, 2400, die, null, null, null)) ]},
        ]);

        // ─── TAKEUCHI ─────────────────────────────────────────────────────────────
        if (B("Takeuchi") > 0) models.AddRange([
            new Model { BrandId = B("Takeuchi"), Name = "TB216", Slug = "takeuchi-tb216", Generations = [
                G("TB216 (2018–)", "takeuchi-tb216-2018", 2018, null,
                    E("Yanmar 3TNV70 13 KM", 13, 10, 854, die, null, null, null)) ]},
            new Model { BrandId = B("Takeuchi"), Name = "TB260", Slug = "takeuchi-tb260", Generations = [
                G("TB260 (2015–)", "takeuchi-tb260-2015", 2015, null,
                    E("Yanmar 4TNV94 47 KM", 47, 35, 2196, die, null, null, null)) ]},
        ]);

        if (models.Count == 0) return;

        db.Models.AddRange(models);
        db.SaveChanges();
        logger.LogInformation("Seeded {Count} models with generations and engine versions", models.Count);
    }
}
