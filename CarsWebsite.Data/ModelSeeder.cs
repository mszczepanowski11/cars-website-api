using CarsWebsite;
using cars_website_api.CarsWebsite.Domain.Entities;

namespace cars_website_api.CarsWebsite.Data;

public static class ModelSeeder
{
    public static void SeedModelsGenerationsEngines(AppDbContext db, ILogger logger)
    {
        if (db.Models.Any()) return;

        var brands = db.Brands.ToDictionary(b => b.Name, b => b.Id);
        var fuels  = db.FuelTypes.ToDictionary(f => f.Name, f => f.Id);
        if (!brands.Any() || !fuels.Any()) return;

        int B(string n) => brands.TryGetValue(n, out var id) ? id : 0;
        int F(string n) => fuels.TryGetValue(n, out var id) ? id : 0;

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
        if (B("Volkswagen") > 0) models.AddRange([
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
        if (B("Skoda") > 0) models.AddRange([
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
        if (B("Audi") > 0) models.AddRange([
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
        if (B("BMW") > 0) models.AddRange([
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
        if (B("Mercedes-Benz") > 0) models.AddRange([
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
                    E("eSprintersElektryczny 115 KM", 115, 85, null, ev)) ]},
        ]);

        // ─── TOYOTA ───────────────────────────────────────────────────────────────
        if (B("Toyota") > 0) models.AddRange([
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
        if (B("Ford") > 0) models.AddRange([
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
        if (B("Opel") > 0) models.AddRange([
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
                    E("1.2 Turbo 130 KM", 130, 96, 1199, ben, 7.5m, 5.0m, 6.0m), E("Corsa-e Elektryczny 136 KM", 136, 100, null, ev)) ]},
        ]);

        // ─── RENAULT ──────────────────────────────────────────────────────────────
        if (B("Renault") > 0) models.AddRange([
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
        if (B("Hyundai") > 0) models.AddRange([
            new Model { BrandId = B("Hyundai"), Name = "i30", Slug = "hyundai-i30", Generations = [
                G("PD (2016–)", "hyundai-i30-pd", 2016, null,
                    E("1.0 T-GDI 100 KM", 100, 74, 998, ben, 8.0m, 5.5m, 6.5m), E("1.0 T-GDI 120 KM", 120, 88, 998, ben, 8.0m, 5.5m, 6.5m),
                    E("1.4 T-GDI 140 KM", 140, 103, 1353, ben, 8.5m, 5.5m, 6.5m), E("N 275 KM", 275, 202, 1998, ben, 12.0m, 8.0m, 10.0m),
                    E("1.4 CRDi 90 KM", 90, 66, 1396, die, 5.5m, 4.0m, 4.5m), E("1.6 CRDi 115 KM", 115, 85, 1582, die, 5.5m, 4.0m, 4.5m)) ]},
            new Model { BrandId = B("Hyundai"), Name = "Tucson", Slug = "hyundai-tucson", Generations = [
                G("III (2015–2020)", "hyundai-tucson-iii", 2015, 2020,
                    E("1.6 T-GDI 132 KM", 132, 97, 1591, ben), E("1.6 T-GDI 177 KM", 177, 130, 1591, ben),
                    E("1.7 CRDi 116 KM", 116, 85, 1685, die), E("2.0 CRDi 136 KM", 136, 100, 1995, die),
                    E("2.0 CRDi 185 KM", 185, 136, 1995, die)),
                G("IV (2020–)", "hyundai-tucson-iv", 2020, null,
                    E("1.6 T-GDI 150 KM", 150, 110, 1591, mild), E("1.6 T-GDI Hybrid 230 KM", 230, 169, 1591, hyb),
                    E("1.6 CRDi 115 KM", 115, 85, 1598, mild), E("1.6 CRDi 136 KM", 136, 100, 1598, mild),
                    E("PHEV 265 KM", 265, 195, 1591, phev)) ]},
            new Model { BrandId = B("Hyundai"), Name = "Kona", Slug = "hyundai-kona", Generations = [
                G("I (2017–)", "hyundai-kona-i", 2017, null,
                    E("1.0 T-GDI 120 KM", 120, 88, 998, ben), E("1.6 T-GDI 177 KM", 177, 130, 1591, ben),
                    E("1.6 CRDi 115 KM", 115, 85, 1598, die),
                    E("Electric 39 kWh 136 KM", 136, 100, null, ev),
                    E("Electric 64 kWh 204 KM", 204, 150, null, ev)) ]},
        ]);

        // ─── KIA ──────────────────────────────────────────────────────────────────
        if (B("Kia") > 0) models.AddRange([
            new Model { BrandId = B("Kia"), Name = "Ceed", Slug = "kia-ceed", Generations = [
                G("III (2018–)", "kia-ceed-iii", 2018, null,
                    E("1.0 T-GDI 100 KM", 100, 74, 998, mild), E("1.0 T-GDI 120 KM", 120, 88, 998, mild),
                    E("1.4 T-GDI 140 KM", 140, 103, 1353, mild), E("GT 1.6 T-GDI 204 KM", 204, 150, 1591, ben),
                    E("1.4 CRDi 90 KM", 90, 66, 1396, die), E("1.6 CRDi 115 KM", 115, 85, 1598, die)) ]},
            new Model { BrandId = B("Kia"), Name = "Sportage", Slug = "kia-sportage", Generations = [
                G("IV (2015–2021)", "kia-sportage-iv", 2015, 2021,
                    E("1.6 T-GDI 132 KM", 132, 97, 1591, ben), E("2.0 MPI 163 KM", 163, 120, 1998, ben),
                    E("1.7 CRDi 115 KM", 115, 85, 1685, die), E("2.0 CRDi 136 KM", 136, 100, 1999, die),
                    E("2.0 CRDi 185 KM", 185, 136, 1999, die)),
                G("V (2021–)", "kia-sportage-v", 2021, null,
                    E("1.6 T-GDI 150 KM", 150, 110, 1591, mild), E("1.6 T-GDI HEV 230 KM", 230, 169, 1591, hyb),
                    E("1.6 CRDI 115 KM", 115, 85, 1598, mild), E("1.6 CRDI 136 KM", 136, 100, 1598, mild),
                    E("PHEV 265 KM", 265, 195, 1591, phev)) ]},
            new Model { BrandId = B("Kia"), Name = "EV6", Slug = "kia-ev6", Generations = [
                G("CV (2021–)", "kia-ev6-cv", 2021, null,
                    E("58 kWh RWD 170 KM", 170, 125, null, ev),
                    E("77.4 kWh RWD 229 KM", 229, 168, null, ev),
                    E("77.4 kWh AWD 325 KM", 325, 239, null, ev),
                    E("GT 77.4 kWh AWD 585 KM", 585, 430, null, ev)) ]},
        ]);

        // ─── DACIA ────────────────────────────────────────────────────────────────
        if (B("Dacia") > 0) models.AddRange([
            new Model { BrandId = B("Dacia"), Name = "Duster", Slug = "dacia-duster", Generations = [
                G("II (2017–2023)", "dacia-duster-ii", 2017, 2023,
                    E("1.0 TCe 90 KM", 90, 66, 999, ben), E("1.3 TCe 130 KM", 130, 96, 1332, ben),
                    E("1.5 dCi 90 KM", 90, 66, 1461, die), E("1.5 dCi 115 KM", 115, 85, 1461, die),
                    E("Bifuel LPG 100 KM", 100, 74, 999, F("LPG"))),
                G("III (2023–)", "dacia-duster-iii", 2023, null,
                    E("1.2 TCe 130 KM", 130, 96, 1199, ben), E("1.2 TCe Hybrid 140 KM", 140, 103, 1199, hyb),
                    E("Bifuel LPG 100 KM", 100, 74, 999, F("LPG"))) ]},
            new Model { BrandId = B("Dacia"), Name = "Sandero", Slug = "dacia-sandero", Generations = [
                G("III (2020–)", "dacia-sandero-iii", 2020, null,
                    E("1.0 SCe 65 KM", 65, 48, 999, ben), E("1.0 TCe 90 KM", 90, 66, 999, ben),
                    E("1.0 TCe 100 KM", 100, 74, 999, ben), E("Bifuel LPG 100 KM", 100, 74, 999, F("LPG")),
                    E("Stepway TCe 110 KM", 110, 81, 999, ben)) ]},
            new Model { BrandId = B("Dacia"), Name = "Spring", Slug = "dacia-spring", Generations = [
                G("I (2021–)", "dacia-spring-i", 2021, null,
                    E("Electric 27.4 kWh 65 KM", 65, 48, null, ev),
                    E("Electric 33 kWh 65 KM", 65, 48, null, ev)) ]},
        ]);

        // ─── FIAT ─────────────────────────────────────────────────────────────────
        if (B("Fiat") > 0) models.AddRange([
            new Model { BrandId = B("Fiat"), Name = "500", Slug = "fiat-500", Generations = [
                G("312 (2007–2024)", "fiat-500-312", 2007, 2024,
                    E("0.9 TwinAir 80 KM", 80, 59, 875, ben), E("1.0 Hybrid 70 KM", 70, 51, 999, mild),
                    E("1.2 69 KM", 69, 51, 1242, ben), E("Abarth 1.4 T-Jet 145 KM", 145, 107, 1368, ben)),
                G("332 elettrica (2020–)", "fiat-500e-332", 2020, null,
                    E("Electric 24 kWh 95 KM", 95, 70, null, ev),
                    E("Electric 42 kWh 118 KM", 118, 87, null, ev)) ]},
            new Model { BrandId = B("Fiat"), Name = "Panda", Slug = "fiat-panda", Generations = [
                G("319 (2011–)", "fiat-panda-319", 2011, null,
                    E("1.2 69 KM", 69, 51, 1242, ben), E("0.9 TwinAir 85 KM", 85, 63, 875, ben),
                    E("1.0 Hybrid 70 KM", 70, 51, 999, mild),
                    E("1.3 Multijet 75 KM", 75, 55, 1248, die)) ]},
            new Model { BrandId = B("Fiat"), Name = "Tipo", Slug = "fiat-tipo", Generations = [
                G("356 (2015–)", "fiat-tipo-356", 2015, null,
                    E("1.0 100 KM", 100, 74, 999, ben), E("1.4 95 KM", 95, 70, 1368, ben),
                    E("1.4 Turbo 120 KM", 120, 88, 1368, ben),
                    E("1.3 MultiJet 90 KM", 90, 66, 1248, die), E("1.6 MultiJet 120 KM", 120, 88, 1598, die)) ]},
        ]);

        // ─── SEAT ─────────────────────────────────────────────────────────────────
        if (B("Seat") > 0) models.AddRange([
            new Model { BrandId = B("Seat"), Name = "Leon", Slug = "seat-leon", Generations = [
                G("5F (2012–2019)", "seat-leon-5f", 2012, 2019,
                    E("1.0 TSI 115 KM", 115, 85, 999, ben), E("1.4 TSI 125 KM", 125, 92, 1395, ben),
                    E("1.5 TSI 130 KM", 130, 96, 1498, ben), E("Cupra 300 KM", 300, 221, 1984, ben),
                    E("1.6 TDI 115 KM", 115, 85, 1598, die), E("2.0 TDI 150 KM", 150, 110, 1968, die)),
                G("KL8 (2020–)", "seat-leon-kl8", 2020, null,
                    E("1.0 eTSI 110 KM", 110, 81, 999, mild), E("1.5 TSI 130 KM", 130, 96, 1498, mild),
                    E("2.0 TSI FR 190 KM", 190, 140, 1984, mild),
                    E("2.0 TDI 115 KM", 115, 85, 1968, die), E("2.0 TDI 150 KM", 150, 110, 1968, die),
                    E("e-Hybrid PHEV 204 KM", 204, 150, 1395, phev)) ]},
            new Model { BrandId = B("Seat"), Name = "Ibiza", Slug = "seat-ibiza", Generations = [
                G("V (2017–)", "seat-ibiza-v", 2017, null,
                    E("1.0 MPI 65 KM", 65, 48, 999, ben), E("1.0 TSI 95 KM", 95, 70, 999, ben),
                    E("1.0 TSI 110 KM", 110, 81, 999, ben), E("FR 1.5 TSI 150 KM", 150, 110, 1498, ben)) ]},
            new Model { BrandId = B("Seat"), Name = "Ateca", Slug = "seat-ateca", Generations = [
                G("I (2016–)", "seat-ateca-i", 2016, null,
                    E("1.0 TSI 115 KM", 115, 85, 999, ben), E("1.5 TSI 150 KM", 150, 110, 1498, ben),
                    E("2.0 TSI 190 KM", 190, 140, 1984, ben), E("Cupra 300 KM", 300, 221, 1984, ben),
                    E("1.6 TDI 115 KM", 115, 85, 1598, die), E("2.0 TDI 150 KM", 150, 110, 1968, die)) ]},
        ]);

        // ─── NISSAN ───────────────────────────────────────────────────────────────
        if (B("Nissan") > 0) models.AddRange([
            new Model { BrandId = B("Nissan"), Name = "Qashqai", Slug = "nissan-qashqai", Generations = [
                G("J11 (2013–2021)", "nissan-qashqai-j11", 2013, 2021,
                    E("1.2 DIG-T 115 KM", 115, 85, 1197, ben), E("1.6 DIG-T 163 KM", 163, 120, 1598, ben),
                    E("1.5 dCi 110 KM", 110, 81, 1461, die), E("1.6 dCi 130 KM", 130, 96, 1598, die)),
                G("J12 (2021–)", "nissan-qashqai-j12", 2021, null,
                    E("1.3 DIG-T 140 KM", 140, 103, 1332, mild), E("1.3 DIG-T 158 KM", 158, 116, 1332, mild),
                    E("e-Power HEV 190 KM", 190, 140, 1497, hyb)) ]},
            new Model { BrandId = B("Nissan"), Name = "Leaf", Slug = "nissan-leaf", Generations = [
                G("II (2017–)", "nissan-leaf-ii", 2017, null,
                    E("40 kWh 150 KM", 150, 110, null, ev),
                    E("62 kWh e+ 217 KM", 217, 160, null, ev)) ]},
            new Model { BrandId = B("Nissan"), Name = "Juke", Slug = "nissan-juke", Generations = [
                G("F16 (2019–)", "nissan-juke-f16", 2019, null,
                    E("1.0 DIG-T 114 KM", 114, 84, 999, ben),
                    E("Hybrid 143 KM", 143, 105, 1598, hyb)) ]},
        ]);

        // ─── HONDA ────────────────────────────────────────────────────────────────
        if (B("Honda") > 0) models.AddRange([
            new Model { BrandId = B("Honda"), Name = "Civic", Slug = "honda-civic", Generations = [
                G("X (2017–2021)", "honda-civic-x", 2017, 2021,
                    E("1.0 VTEC Turbo 126 KM", 126, 93, 988, ben), E("1.5 VTEC Turbo 182 KM", 182, 134, 1498, ben),
                    E("Type R 2.0 320 KM", 320, 235, 1996, ben), E("1.6 i-DTEC 120 KM", 120, 88, 1597, die)),
                G("XI (2021–)", "honda-civic-xi", 2021, null,
                    E("1.5 VTEC Turbo 182 KM", 182, 134, 1498, ben),
                    E("Type R 2.0 329 KM", 329, 242, 1996, ben),
                    E("e:HEV 2.0 143 KM", 143, 105, 1993, hyb)) ]},
            new Model { BrandId = B("Honda"), Name = "CR-V", Slug = "honda-crv", Generations = [
                G("IV (2012–2018)", "honda-crv-iv", 2012, 2018,
                    E("1.6 i-DTEC 120 KM", 120, 88, 1597, die), E("1.6 i-DTEC 160 KM", 160, 118, 1597, die),
                    E("2.0 i-VTEC 155 KM", 155, 114, 1997, ben)),
                G("V (2018–)", "honda-crv-v", 2018, null,
                    E("1.5 VTEC Turbo 173 KM", 173, 127, 1498, ben),
                    E("e:HEV 2.0 HEV 184 KM", 184, 135, 1993, hyb),
                    E("PHEV 325 KM", 325, 239, 2000, phev)) ]},
        ]);

        // ─── VOLVO ────────────────────────────────────────────────────────────────
        if (B("Volvo") > 0) models.AddRange([
            new Model { BrandId = B("Volvo"), Name = "FH", Slug = "volvo-fh", Generations = [
                G("IV (2012–2020)", "volvo-fh-iv", 2012, 2020,
                    E("D13 420 KM", 420, 309, 12777, die), E("D13 460 KM", 460, 338, 12777, die),
                    E("D13 500 KM", 500, 368, 12777, die), E("D16 550 KM", 550, 404, 16120, die)),
                G("V (2020–)", "volvo-fh-v", 2020, null,
                    E("D13 420 KM", 420, 309, 12777, die), E("D13 460 KM", 460, 338, 12777, die),
                    E("D13 500 KM", 500, 368, 12777, die), E("FH Electric 490 KM", 490, 360, null, ev)) ]},
            new Model { BrandId = B("Volvo"), Name = "FM", Slug = "volvo-fm", Generations = [
                G("IV (2012–)", "volvo-fm-iv", 2012, null,
                    E("D11 330 KM", 330, 243, 10837, die), E("D11 370 KM", 370, 272, 10837, die),
                    E("D13 430 KM", 430, 316, 12777, die), E("D13 460 KM", 460, 338, 12777, die)) ]},
        ]);

        // ─── MAN ──────────────────────────────────────────────────────────────────
        if (B("MAN") > 0) models.AddRange([
            new Model { BrandId = B("MAN"), Name = "TGX", Slug = "man-tgx", Generations = [
                G("I (2007–2020)", "man-tgx-i", 2007, 2020,
                    E("D2066 400 KM", 400, 294, 10518, die), E("D2066 440 KM", 440, 324, 10518, die),
                    E("D2676 480 KM", 480, 353, 12419, die), E("D2676 520 KM", 520, 382, 12419, die)),
                G("NEO (2020–)", "man-tgx-neo", 2020, null,
                    E("D2676 400 KM", 400, 294, 12419, die), E("D2676 430 KM", 430, 316, 12419, die),
                    E("D2676 470 KM", 470, 346, 12419, die), E("D3876 530 KM", 530, 390, 15249, die)) ]},
            new Model { BrandId = B("MAN"), Name = "TGS", Slug = "man-tgs", Generations = [
                G("I (2007–2020)", "man-tgs-i", 2007, 2020,
                    E("D2066 320 KM", 320, 235, 10518, die), E("D2066 360 KM", 360, 265, 10518, die),
                    E("D2676 400 KM", 400, 294, 12419, die), E("D2676 440 KM", 440, 324, 12419, die)),
                G("II (2020–)", "man-tgs-ii", 2020, null,
                    E("D2676 330 KM", 330, 243, 12419, die), E("D2676 380 KM", 380, 279, 12419, die),
                    E("D2676 430 KM", 430, 316, 12419, die)) ]},
        ]);

        // ─── SCANIA ───────────────────────────────────────────────────────────────
        if (B("Scania") > 0) models.AddRange([
            new Model { BrandId = B("Scania"), Name = "R-Series", Slug = "scania-r-series", Generations = [
                G("R5 (2009–2016)", "scania-r-r5", 2009, 2016,
                    E("DC09 320 KM", 320, 235, 9290, die), E("DC13 420 KM", 420, 309, 12742, die),
                    E("DC13 450 KM", 450, 331, 12742, die), E("DC16 580 KM", 580, 427, 15607, die)),
                G("Next Gen (2016–)", "scania-r-nextgen", 2016, null,
                    E("DC09 320 KM", 320, 235, 9290, die), E("DC13 410 KM", 410, 302, 12742, die),
                    E("DC13 460 KM", 460, 338, 12742, die), E("DC13 500 KM", 500, 368, 12742, die),
                    E("DC16 590 KM", 590, 434, 15607, die)) ]},
            new Model { BrandId = B("Scania"), Name = "S-Series", Slug = "scania-s-series", Generations = [
                G("Next Gen (2016–)", "scania-s-nextgen", 2016, null,
                    E("DC13 410 KM", 410, 302, 12742, die), E("DC13 460 KM", 460, 338, 12742, die),
                    E("DC13 500 KM", 500, 368, 12742, die), E("DC16 590 KM", 590, 434, 15607, die)) ]},
        ]);

        // ─── YAMAHA (motocykle) ───────────────────────────────────────────────────
        if (B("Yamaha") > 0) models.AddRange([
            new Model { BrandId = B("Yamaha"), Name = "MT-07", Slug = "yamaha-mt07", Generations = [
                G("RM04 (2013–2020)", "yamaha-mt07-rm04", 2013, 2020,
                    E("689cc CP2 73 KM", 73, 54, 689, ben)),
                G("RM36 (2021–)", "yamaha-mt07-rm36", 2021, null,
                    E("689cc CP2 73 KM", 73, 54, 689, ben)) ]},
            new Model { BrandId = B("Yamaha"), Name = "MT-09", Slug = "yamaha-mt09", Generations = [
                G("RN29 (2013–2020)", "yamaha-mt09-rn29", 2013, 2020,
                    E("847cc CP3 115 KM", 115, 85, 847, ben)),
                G("RN57 (2021–)", "yamaha-mt09-rn57", 2021, null,
                    E("890cc CP3 119 KM", 119, 88, 890, ben)) ]},
            new Model { BrandId = B("Yamaha"), Name = "Tracer 9", Slug = "yamaha-tracer9", Generations = [
                G("RN57 GT (2021–)", "yamaha-tracer9-rn57", 2021, null,
                    E("890cc CP3 119 KM", 119, 88, 890, ben)) ]},
            new Model { BrandId = B("Yamaha"), Name = "YZF-R1", Slug = "yamaha-r1", Generations = [
                G("RN32 (2015–)", "yamaha-r1-rn32", 2015, null,
                    E("998cc Crossplane 200 KM", 200, 147, 998, ben)) ]},
            new Model { BrandId = B("Yamaha"), Name = "TMAX 560", Slug = "yamaha-tmax560", Generations = [
                G("SJ19 (2020–)", "yamaha-tmax560-sj19", 2020, null,
                    E("562cc parallel twin 47 KM", 47, 35, 562, ben)) ]},
        ]);

        // ─── KAWASAKI ─────────────────────────────────────────────────────────────
        if (B("Kawasaki") > 0) models.AddRange([
            new Model { BrandId = B("Kawasaki"), Name = "Z900", Slug = "kawasaki-z900", Generations = [
                G("ZR900 (2017–)", "kawasaki-z900-zr900", 2017, null,
                    E("948cc inline-4 125 KM", 125, 92, 948, ben)) ]},
            new Model { BrandId = B("Kawasaki"), Name = "Ninja 650", Slug = "kawasaki-ninja650", Generations = [
                G("ER-6 (2017–)", "kawasaki-ninja650-er6", 2017, null,
                    E("649cc parallel twin 68 KM", 68, 50, 649, ben)) ]},
            new Model { BrandId = B("Kawasaki"), Name = "ZX-10R", Slug = "kawasaki-zx10r", Generations = [
                G("2021– (ZX1002L)", "kawasaki-zx10r-2021", 2021, null,
                    E("998cc inline-4 203 KM", 203, 149, 998, ben)) ]},
            new Model { BrandId = B("Kawasaki"), Name = "Versys 650", Slug = "kawasaki-versys650", Generations = [
                G("LE650 (2015–)", "kawasaki-versys650-le650", 2015, null,
                    E("649cc parallel twin 69 KM", 69, 51, 649, ben)) ]},
        ]);

        // ─── DUCATI ───────────────────────────────────────────────────────────────
        if (B("Ducati") > 0) models.AddRange([
            new Model { BrandId = B("Ducati"), Name = "Panigale V4", Slug = "ducati-panigale-v4", Generations = [
                G("2018–", "ducati-panigale-v4-2018", 2018, null,
                    E("Desmosedici Stradale 1103cc 214 KM", 214, 157, 1103, ben),
                    E("V4 S 1103cc 214 KM", 214, 157, 1103, ben),
                    E("V4 R 998cc 221 KM", 221, 163, 998, ben)) ]},
            new Model { BrandId = B("Ducati"), Name = "Monster", Slug = "ducati-monster", Generations = [
                G("937 (2021–)", "ducati-monster-937", 2021, null,
                    E("937cc Testastretta 111 KM", 111, 82, 937, ben)) ]},
            new Model { BrandId = B("Ducati"), Name = "Multistrada V4", Slug = "ducati-multistrada-v4", Generations = [
                G("2021–", "ducati-multistrada-v4-2021", 2021, null,
                    E("1158cc V4 170 KM", 170, 125, 1158, ben),
                    E("V4 S 1158cc 170 KM", 170, 125, 1158, ben)) ]},
            new Model { BrandId = B("Ducati"), Name = "Scrambler 803", Slug = "ducati-scrambler803", Generations = [
                G("2015–", "ducati-scrambler803-2015", 2015, null,
                    E("803cc Desmodue 73 KM", 73, 54, 803, ben)) ]},
        ]);

        // ─── TRIUMPH ──────────────────────────────────────────────────────────────
        if (B("Triumph") > 0) models.AddRange([
            new Model { BrandId = B("Triumph"), Name = "Bonneville T120", Slug = "triumph-bonneville-t120", Generations = [
                G("2016–", "triumph-bonnie-t120-2016", 2016, null,
                    E("1200cc parallel twin 80 KM", 80, 59, 1200, ben)) ]},
            new Model { BrandId = B("Triumph"), Name = "Street Triple 765", Slug = "triumph-street-triple-765", Generations = [
                G("2017–", "triumph-street-triple-2017", 2017, null,
                    E("765cc inline-3 R 118 KM", 118, 87, 765, ben),
                    E("765cc inline-3 RS 130 KM", 130, 96, 765, ben)) ]},
            new Model { BrandId = B("Triumph"), Name = "Tiger 900", Slug = "triumph-tiger-900", Generations = [
                G("2020–", "triumph-tiger900-2020", 2020, null,
                    E("888cc inline-3 95 KM", 95, 70, 888, ben)) ]},
        ]);

        // ─── HARLEY-DAVIDSON ──────────────────────────────────────────────────────
        if (B("Harley-Davidson") > 0) models.AddRange([
            new Model { BrandId = B("Harley-Davidson"), Name = "Sportster S", Slug = "hd-sportster-s", Generations = [
                G("RH1250S (2021–)", "hd-sportster-s-2021", 2021, null,
                    E("Revolution Max 1250T 121 KM", 121, 89, 1252, ben)) ]},
            new Model { BrandId = B("Harley-Davidson"), Name = "Fat Bob", Slug = "hd-fat-bob", Generations = [
                G("FXFBS (2017–)", "hd-fatbob-fxfbs", 2017, null,
                    E("Milwaukee-Eight 107 V-Twin 90 KM", 90, 66, 1745, ben),
                    E("Milwaukee-Eight 114 V-Twin 100 KM", 100, 74, 1868, ben)) ]},
            new Model { BrandId = B("Harley-Davidson"), Name = "Road Glide", Slug = "hd-road-glide", Generations = [
                G("2017–", "hd-road-glide-2017", 2017, null,
                    E("Milwaukee-Eight 107 90 KM", 90, 66, 1745, ben),
                    E("Milwaukee-Eight 114 100 KM", 100, 74, 1868, ben)) ]},
        ]);

        // ─── KTM ──────────────────────────────────────────────────────────────────
        if (B("KTM") > 0) models.AddRange([
            new Model { BrandId = B("KTM"), Name = "390 Duke", Slug = "ktm-390-duke", Generations = [
                G("2017–", "ktm-390duke-2017", 2017, null,
                    E("373cc single-cylinder 44 KM", 44, 32, 373, ben)) ]},
            new Model { BrandId = B("KTM"), Name = "790 Duke", Slug = "ktm-790-duke", Generations = [
                G("2018–", "ktm-790duke-2018", 2018, null,
                    E("799cc LC8c parallel twin 105 KM", 105, 77, 799, ben)) ]},
            new Model { BrandId = B("KTM"), Name = "1290 Super Duke R", Slug = "ktm-1290-super-duke-r", Generations = [
                G("2020–", "ktm-1290sdr-2020", 2020, null,
                    E("1301cc LC8 V-twin 180 KM", 180, 132, 1301, ben)) ]},
        ]);

        // ─── JOHN DEERE ───────────────────────────────────────────────────────────
        if (B("John Deere") > 0) models.AddRange([
            new Model { BrandId = B("John Deere"), Name = "Seria 6", Slug = "jd-seria-6", Generations = [
                G("6M/6R (2014–)", "jd-seria6-2014", 2014, null,
                    E("6110R 110 KM", 110, 81, 4530, die), E("6130R 130 KM", 130, 96, 4530, die),
                    E("6155R 155 KM", 155, 114, 6800, die), E("6175R 175 KM", 175, 129, 6800, die)) ]},
            new Model { BrandId = B("John Deere"), Name = "Seria 7", Slug = "jd-seria-7", Generations = [
                G("7R (2014–)", "jd-seria7-2014", 2014, null,
                    E("7230R 230 KM", 230, 169, 9000, die), E("7260R 260 KM", 260, 191, 9000, die),
                    E("7310R 310 KM", 310, 228, 9000, die)) ]},
        ]);

        // ─── FENDT ────────────────────────────────────────────────────────────────
        if (B("Fendt") > 0) models.AddRange([
            new Model { BrandId = B("Fendt"), Name = "Vario 700", Slug = "fendt-vario-700", Generations = [
                G("Gen6 (2014–)", "fendt-700-gen6", 2014, null,
                    E("718 Vario 177 KM", 177, 130, 6057, die), E("720 Vario 200 KM", 200, 147, 6057, die),
                    E("724 Vario 240 KM", 240, 177, 6057, die), E("728 Vario 281 KM", 281, 207, 6057, die)) ]},
            new Model { BrandId = B("Fendt"), Name = "Vario 900", Slug = "fendt-vario-900", Generations = [
                G("Gen6 (2014–)", "fendt-900-gen6", 2014, null,
                    E("927 Vario 270 KM", 270, 199, 8400, die), E("930 Vario 300 KM", 300, 221, 8400, die),
                    E("936 Vario 360 KM", 360, 265, 8400, die), E("939 Vario 390 KM", 390, 287, 8400, die)) ]},
        ]);

        // ─── NEW HOLLAND ──────────────────────────────────────────────────────────
        if (B("New Holland") > 0) models.AddRange([
            new Model { BrandId = B("New Holland"), Name = "T6", Slug = "nh-t6", Generations = [
                G("T6.xxx (2014–)", "nh-t6-2014", 2014, null,
                    E("T6.140 140 KM", 140, 103, 4485, die), E("T6.160 160 KM", 160, 118, 4485, die),
                    E("T6.180 180 KM", 180, 132, 6728, die)) ]},
            new Model { BrandId = B("New Holland"), Name = "T7", Slug = "nh-t7", Generations = [
                G("T7.xxx (2014–)", "nh-t7-2014", 2014, null,
                    E("T7.175 175 KM", 175, 129, 6728, die), E("T7.210 210 KM", 210, 154, 6728, die),
                    E("T7.260 260 KM", 260, 191, 8728, die)) ]},
        ]);

        // ─── DAF ──────────────────────────────────────────────────────────────────
        if (B("DAF") > 0) models.AddRange([
            new Model { BrandId = B("DAF"), Name = "XF", Slug = "daf-xf", Generations = [
                G("XF105 (2006–2017)", "daf-xf105-2006", 2006, 2017,
                    E("MX-340 340 KM", 340, 250, 12902, die), E("MX-375 375 KM", 375, 276, 12902, die),
                    E("MX-410 410 KM", 410, 302, 12902, die), E("MX-460 460 KM", 460, 338, 12902, die)),
                G("XF NG (2017–)", "daf-xf-ng", 2017, null,
                    E("MX-375 375 KM", 375, 276, 12902, die), E("MX-430 430 KM", 430, 316, 12902, die),
                    E("MX-480 480 KM", 480, 353, 12902, die), E("MX-530 530 KM", 530, 390, 12902, die)) ]},
            new Model { BrandId = B("DAF"), Name = "CF", Slug = "daf-cf", Generations = [
                G("CF85 (2001–2017)", "daf-cf85-2001", 2001, 2017,
                    E("MX-300 300 KM", 300, 221, 12902, die), E("MX-340 340 KM", 340, 250, 12902, die),
                    E("MX-375 375 KM", 375, 276, 12902, die), E("MX-410 410 KM", 410, 302, 12902, die)),
                G("CF NG (2017–)", "daf-cf-ng", 2017, null,
                    E("MX-310 310 KM", 310, 228, 12902, die), E("MX-350 350 KM", 350, 257, 12902, die),
                    E("MX-395 395 KM", 395, 291, 12902, die), E("MX-460 460 KM", 460, 338, 12902, die)) ]},
            new Model { BrandId = B("DAF"), Name = "LF", Slug = "daf-lf", Generations = [
                G("LF (2006–)", "daf-lf-2006", 2006, null,
                    E("PX-5 150 KM", 150, 110, 4486, die), E("PX-5 180 KM", 180, 132, 4486, die),
                    E("PX-7 210 KM", 210, 154, 6728, die), E("PX-7 220 KM", 220, 162, 6728, die)) ]},
        ]);

        // ─── IVECO ────────────────────────────────────────────────────────────────
        if (B("Iveco") > 0) models.AddRange([
            new Model { BrandId = B("Iveco"), Name = "Stralis", Slug = "iveco-stralis", Generations = [
                G("Stralis I (2002–2012)", "iveco-stralis-i", 2002, 2012,
                    E("Cursor 10 400 KM", 400, 294, 10308, die), E("Cursor 10 430 KM", 430, 316, 10308, die),
                    E("Cursor 13 480 KM", 480, 353, 12882, die), E("Cursor 13 500 KM", 500, 368, 12882, die)),
                G("Stralis Hi-Way (2012–)", "iveco-stralis-hiway", 2012, null,
                    E("Cursor 10 420 KM", 420, 309, 10308, die), E("Cursor 11 460 KM", 460, 338, 10874, die),
                    E("Cursor 13 500 KM", 500, 368, 12882, die), E("Cursor 13 560 KM", 560, 412, 12882, die)) ]},
            new Model { BrandId = B("Iveco"), Name = "Eurocargo", Slug = "iveco-eurocargo", Generations = [
                G("Eurocargo (2008–)", "iveco-eurocargo-2008", 2008, null,
                    E("F4AE 150 KM", 150, 110, 3920, die), E("F4AE 180 KM", 180, 132, 3920, die),
                    E("F4AE 220 KM", 220, 162, 5880, die), E("F4AE 250 KM", 250, 184, 5880, die),
                    E("F4AE 280 KM", 280, 206, 5880, die)) ]},
            new Model { BrandId = B("Iveco"), Name = "Daily", Slug = "iveco-daily", Generations = [
                G("VI (2014–)", "iveco-daily-vi", 2014, null,
                    E("F1A 120 KM", 120, 88, 2287, die), E("F1A 140 KM", 140, 103, 2287, die),
                    E("F1C 160 KM", 160, 118, 2998, die), E("F1C 180 KM", 180, 132, 2998, die),
                    E("F1C 210 KM", 210, 154, 2998, die)) ]},
        ]);

        // ─── RENAULT TRUCKS ───────────────────────────────────────────────────────
        if (B("Renault Trucks") > 0) models.AddRange([
            new Model { BrandId = B("Renault Trucks"), Name = "T-series", Slug = "renault-trucks-t", Generations = [
                G("T (2013–)", "renault-trucks-t-2013", 2013, null,
                    E("DTI11 380 KM", 380, 279, 10837, die), E("DTI11 430 KM", 430, 316, 10837, die),
                    E("DTI13 480 KM", 480, 353, 12777, die), E("DTI13 520 KM", 520, 382, 12777, die)) ]},
            new Model { BrandId = B("Renault Trucks"), Name = "C-series", Slug = "renault-trucks-c", Generations = [
                G("C (2013–)", "renault-trucks-c-2013", 2013, null,
                    E("DTI8 330 KM", 330, 243, 7696, die), E("DTI8 370 KM", 370, 272, 7696, die),
                    E("DTI11 410 KM", 410, 302, 10837, die), E("DTI11 460 KM", 460, 338, 10837, die)) ]},
            new Model { BrandId = B("Renault Trucks"), Name = "D-series", Slug = "renault-trucks-d", Generations = [
                G("D (2013–)", "renault-trucks-d-2013", 2013, null,
                    E("DTI5 210 KM", 210, 154, 4769, die), E("DTI8 250 KM", 250, 184, 7696, die),
                    E("DTI8 280 KM", 280, 206, 7696, die), E("DTI8 330 KM", 330, 243, 7696, die)) ]},
        ]);

        // ─── BMW (motorcycles) ────────────────────────────────────────────────────
        if (B("BMW") > 0) models.AddRange([
            new Model { BrandId = B("BMW"), Name = "R 1250 GS", Slug = "bmw-r1250gs", Generations = [
                G("2018–", "bmw-r1250gs-2018", 2018, null,
                    E("1254cc Boxer 136 KM", 136, 100, 1254, ben)) ]},
            new Model { BrandId = B("BMW"), Name = "F 900 R", Slug = "bmw-f900r", Generations = [
                G("2020–", "bmw-f900r-2020", 2020, null,
                    E("895cc parallel twin 105 KM", 105, 77, 895, ben)) ]},
            new Model { BrandId = B("BMW"), Name = "S 1000 RR", Slug = "bmw-s1000rr", Generations = [
                G("2009–2018", "bmw-s1000rr-2009", 2009, 2018,
                    E("999cc inline-4 193 KM", 193, 142, 999, ben)),
                G("2019–", "bmw-s1000rr-2019", 2019, null,
                    E("999cc inline-4 210 KM", 210, 154, 999, ben)) ]},
            new Model { BrandId = B("BMW"), Name = "R nineT", Slug = "bmw-r-ninet", Generations = [
                G("2014–", "bmw-r-ninet-2014", 2014, null,
                    E("1170cc Boxer 109 KM", 109, 80, 1170, ben)) ]},
        ]);

        // ─── HONDA (motorcycles) ──────────────────────────────────────────────────
        if (B("Honda") > 0) models.AddRange([
            new Model { BrandId = B("Honda"), Name = "CB500F", Slug = "honda-cb500f", Generations = [
                G("2013–", "honda-cb500f-2013", 2013, null,
                    E("471cc parallel twin 47 KM", 47, 35, 471, ben)) ]},
            new Model { BrandId = B("Honda"), Name = "Africa Twin CRF1100L", Slug = "honda-africa-twin-crf1100l", Generations = [
                G("2020–", "honda-africa-twin-2020", 2020, null,
                    E("1084cc parallel twin 101 KM", 101, 74, 1084, ben)) ]},
            new Model { BrandId = B("Honda"), Name = "CBR1000RR Fireblade", Slug = "honda-cbr1000rr-fireblade", Generations = [
                G("2017–", "honda-cbr1000rr-2017", 2017, null,
                    E("999cc inline-4 214 KM", 214, 157, 999, ben)) ]},
            new Model { BrandId = B("Honda"), Name = "Gold Wing GL1800", Slug = "honda-gold-wing-gl1800", Generations = [
                G("2018–", "honda-gold-wing-2018", 2018, null,
                    E("1833cc flat-6 126 KM", 126, 93, 1833, ben)) ]},
            new Model { BrandId = B("Honda"), Name = "CB650R", Slug = "honda-cb650r", Generations = [
                G("2019–", "honda-cb650r-2019", 2019, null,
                    E("649cc inline-4 95 KM", 95, 70, 649, ben)) ]},
        ]);

        // ─── SUZUKI (motorcycles) ─────────────────────────────────────────────────
        if (B("Suzuki") > 0) models.AddRange([
            new Model { BrandId = B("Suzuki"), Name = "GSX-R1000", Slug = "suzuki-gsx-r1000", Generations = [
                G("2017–", "suzuki-gsx-r1000-2017", 2017, null,
                    E("999cc inline-4 202 KM", 202, 149, 999, ben)) ]},
            new Model { BrandId = B("Suzuki"), Name = "V-Strom 650", Slug = "suzuki-v-strom-650", Generations = [
                G("2017–", "suzuki-v-strom-650-2017", 2017, null,
                    E("645cc V-twin 71 KM", 71, 52, 645, ben)) ]},
            new Model { BrandId = B("Suzuki"), Name = "GSX-S1000", Slug = "suzuki-gsx-s1000", Generations = [
                G("2021–", "suzuki-gsx-s1000-2021", 2021, null,
                    E("999cc inline-4 152 KM", 152, 112, 999, ben)) ]},
            new Model { BrandId = B("Suzuki"), Name = "Hayabusa", Slug = "suzuki-hayabusa", Generations = [
                G("2021–", "suzuki-hayabusa-2021", 2021, null,
                    E("1340cc inline-4 190 KM", 190, 140, 1340, ben)) ]},
        ]);

        // ─── APRILIA ──────────────────────────────────────────────────────────────
        if (B("Aprilia") > 0) models.AddRange([
            new Model { BrandId = B("Aprilia"), Name = "RSV4", Slug = "aprilia-rsv4", Generations = [
                G("2021–", "aprilia-rsv4-2021", 2021, null,
                    E("1099cc V4 217 KM", 217, 160, 1099, ben)) ]},
            new Model { BrandId = B("Aprilia"), Name = "Tuono V4", Slug = "aprilia-tuono-v4", Generations = [
                G("2021–", "aprilia-tuono-v4-2021", 2021, null,
                    E("1099cc V4 175 KM", 175, 129, 1099, ben)) ]},
            new Model { BrandId = B("Aprilia"), Name = "RS 660", Slug = "aprilia-rs-660", Generations = [
                G("2020–", "aprilia-rs-660-2020", 2020, null,
                    E("659cc parallel twin 100 KM", 100, 74, 659, ben)) ]},
        ]);

        // ─── ROYAL ENFIELD ────────────────────────────────────────────────────────
        if (B("Royal Enfield") > 0) models.AddRange([
            new Model { BrandId = B("Royal Enfield"), Name = "Classic 350", Slug = "royal-enfield-classic-350", Generations = [
                G("2021–", "royal-enfield-classic-350-2021", 2021, null,
                    E("349cc single-cylinder 20 KM", 20, 15, 349, ben)) ]},
            new Model { BrandId = B("Royal Enfield"), Name = "Himalayan 450", Slug = "royal-enfield-himalayan-450", Generations = [
                G("2024–", "royal-enfield-himalayan-450-2024", 2024, null,
                    E("452cc single-cylinder 40 KM", 40, 29, 452, ben)) ]},
            new Model { BrandId = B("Royal Enfield"), Name = "Meteor 350", Slug = "royal-enfield-meteor-350", Generations = [
                G("2020–", "royal-enfield-meteor-350-2020", 2020, null,
                    E("349cc single-cylinder 20 KM", 20, 15, 349, ben)) ]},
        ]);

        // ─── INDIAN ───────────────────────────────────────────────────────────────
        if (B("Indian") > 0) models.AddRange([
            new Model { BrandId = B("Indian"), Name = "Scout", Slug = "indian-scout", Generations = [
                G("2015–", "indian-scout-2015", 2015, null,
                    E("1133cc V-twin 100 KM", 100, 74, 1133, ben)) ]},
            new Model { BrandId = B("Indian"), Name = "Chief", Slug = "indian-chief", Generations = [
                G("2021–", "indian-chief-2021", 2021, null,
                    E("1890cc Thunderstroke 116 V-twin 93 KM", 93, 68, 1890, ben)) ]},
            new Model { BrandId = B("Indian"), Name = "Challenger", Slug = "indian-challenger", Generations = [
                G("2020–", "indian-challenger-2020", 2020, null,
                    E("1768cc PowerPlus V-twin 122 KM", 122, 90, 1768, ben)) ]},
        ]);

        // ─── MV AGUSTA ────────────────────────────────────────────────────────────
        if (B("MV Agusta") > 0) models.AddRange([
            new Model { BrandId = B("MV Agusta"), Name = "Brutale 800", Slug = "mv-agusta-brutale-800", Generations = [
                G("2012–", "mv-agusta-brutale-800-2012", 2012, null,
                    E("798cc inline-3 140 KM", 140, 103, 798, ben)) ]},
            new Model { BrandId = B("MV Agusta"), Name = "Turismo Veloce 800", Slug = "mv-agusta-turismo-veloce-800", Generations = [
                G("2015–", "mv-agusta-turismo-veloce-2015", 2015, null,
                    E("798cc inline-3 110 KM", 110, 81, 798, ben)) ]},
        ]);

        // ─── HUSQVARNA ────────────────────────────────────────────────────────────
        if (B("Husqvarna") > 0) models.AddRange([
            new Model { BrandId = B("Husqvarna"), Name = "Vitpilen 401", Slug = "husqvarna-vitpilen-401", Generations = [
                G("2018–", "husqvarna-vitpilen-401-2018", 2018, null,
                    E("373cc single-cylinder 44 KM", 44, 32, 373, ben)) ]},
            new Model { BrandId = B("Husqvarna"), Name = "Svartpilen 401", Slug = "husqvarna-svartpilen-401", Generations = [
                G("2018–", "husqvarna-svartpilen-401-2018", 2018, null,
                    E("373cc single-cylinder 44 KM", 44, 32, 373, ben)) ]},
            new Model { BrandId = B("Husqvarna"), Name = "Norden 901", Slug = "husqvarna-norden-901", Generations = [
                G("2022–", "husqvarna-norden-901-2022", 2022, null,
                    E("889cc parallel twin 105 KM", 105, 77, 889, ben)) ]},
        ]);

        // ─── CASE IH ──────────────────────────────────────────────────────────────
        if (B("Case IH") > 0) models.AddRange([
            new Model { BrandId = B("Case IH"), Name = "Puma", Slug = "case-ih-puma", Generations = [
                G("Puma (2014–)", "case-ih-puma-2014", 2014, null,
                    E("FPT F5H 150 KM", 150, 110, 4485, die), E("FPT F5H 185 KM", 185, 136, 4485, die),
                    E("FPT F5H 210 KM", 210, 154, 4485, die), E("FPT Cursor 9 240 KM", 240, 177, 8728, die)) ]},
            new Model { BrandId = B("Case IH"), Name = "Maxxum", Slug = "case-ih-maxxum", Generations = [
                G("Maxxum (2014–)", "case-ih-maxxum-2014", 2014, null,
                    E("FPT F5H 110 KM", 110, 81, 4485, die), E("FPT F5H 125 KM", 125, 92, 4485, die),
                    E("FPT F5H 145 KM", 145, 107, 4485, die)) ]},
            new Model { BrandId = B("Case IH"), Name = "Optum", Slug = "case-ih-optum", Generations = [
                G("Optum (2016–)", "case-ih-optum-2016", 2016, null,
                    E("FPT Cursor 9 270 KM", 270, 199, 8728, die), E("FPT Cursor 9 300 KM", 300, 221, 8728, die),
                    E("FPT Cursor 9 340 KM", 340, 250, 8728, die)) ]},
        ]);

        // ─── CLAAS ────────────────────────────────────────────────────────────────
        if (B("Claas") > 0) models.AddRange([
            new Model { BrandId = B("Claas"), Name = "Axion 800", Slug = "claas-axion-800", Generations = [
                G("Axion 800 (2015–)", "claas-axion-800-2015", 2015, null,
                    E("FPT Cursor 9 205 KM", 205, 151, 8728, die), E("FPT Cursor 9 245 KM", 245, 180, 8728, die),
                    E("FPT Cursor 9 270 KM", 270, 199, 8728, die), E("FPT Cursor 9 295 KM", 295, 217, 8728, die)) ]},
            new Model { BrandId = B("Claas"), Name = "Arion 600", Slug = "claas-arion-600", Generations = [
                G("Arion 600 (2015–)", "claas-arion-600-2015", 2015, null,
                    E("FPT F5H 130 KM", 130, 96, 4485, die), E("FPT F5H 155 KM", 155, 114, 4485, die),
                    E("FPT F5H 185 KM", 185, 136, 4485, die)) ]},
            new Model { BrandId = B("Claas"), Name = "Lexion 8000", Slug = "claas-lexion-8000", Generations = [
                G("Lexion 8000 (2019–)", "claas-lexion-8000-2019", 2019, null,
                    E("Mercedes-Benz OM473 476 KM", 476, 350, 15600, die),
                    E("Mercedes-Benz OM473 598 KM", 598, 440, 15600, die),
                    E("Mercedes-Benz OM473 627 KM", 627, 461, 15600, die)) ]},
            new Model { BrandId = B("Claas"), Name = "Jaguar 900", Slug = "claas-jaguar-900", Generations = [
                G("Jaguar 900 (2016–)", "claas-jaguar-900-2016", 2016, null,
                    E("Mercedes-Benz OM471 624 KM", 624, 459, 12799, die),
                    E("Mercedes-Benz OM473 680 KM", 680, 500, 15600, die),
                    E("Mercedes-Benz OM473 730 KM", 730, 537, 15600, die)) ]},
        ]);

        // ─── MASSEY FERGUSON ──────────────────────────────────────────────────────
        if (B("Massey Ferguson") > 0) models.AddRange([
            new Model { BrandId = B("Massey Ferguson"), Name = "5700S", Slug = "massey-ferguson-5700s", Generations = [
                G("5700S (2017–)", "massey-ferguson-5700s-2017", 2017, null,
                    E("AGCO Power 4.4L 105 KM", 105, 77, 4400, die), E("AGCO Power 4.4L 120 KM", 120, 88, 4400, die),
                    E("AGCO Power 4.4L 140 KM", 140, 103, 4400, die)) ]},
            new Model { BrandId = B("Massey Ferguson"), Name = "6700S", Slug = "massey-ferguson-6700s", Generations = [
                G("6700S (2016–)", "massey-ferguson-6700s-2016", 2016, null,
                    E("AGCO Power 6.6L 155 KM", 155, 114, 6600, die), E("AGCO Power 6.6L 175 KM", 175, 129, 6600, die),
                    E("AGCO Power 6.6L 205 KM", 205, 151, 6600, die)) ]},
            new Model { BrandId = B("Massey Ferguson"), Name = "7700S", Slug = "massey-ferguson-7700s", Generations = [
                G("7700S (2016–)", "massey-ferguson-7700s-2016", 2016, null,
                    E("AGCO Power 6.6L 215 KM", 215, 158, 6600, die), E("AGCO Power 6.6L 240 KM", 240, 177, 6600, die),
                    E("AGCO Power 6.6L 270 KM", 270, 199, 6600, die)) ]},
        ]);

        // ─── ZETOR ────────────────────────────────────────────────────────────────
        if (B("Zetor") > 0) models.AddRange([
            new Model { BrandId = B("Zetor"), Name = "Forterra", Slug = "zetor-forterra", Generations = [
                G("Forterra (2010–)", "zetor-forterra-2010", 2010, null,
                    E("Z 1006 100 KM", 100, 74, 6211, die), E("Z 1006 115 KM", 115, 85, 6211, die),
                    E("Z 1006 135 KM", 135, 99, 6211, die), E("Z 1006 150 KM", 150, 110, 6211, die)) ]},
            new Model { BrandId = B("Zetor"), Name = "Proxima", Slug = "zetor-proxima", Generations = [
                G("Proxima (2010–)", "zetor-proxima-2010", 2010, null,
                    E("Z 1006 75 KM", 75, 55, 6211, die), E("Z 1006 90 KM", 90, 66, 6211, die),
                    E("Z 1006 105 KM", 105, 77, 6211, die)) ]},
        ]);

        // ─── KUBOTA ───────────────────────────────────────────────────────────────
        if (B("Kubota") > 0) models.AddRange([
            new Model { BrandId = B("Kubota"), Name = "M7", Slug = "kubota-m7", Generations = [
                G("M7 (2015–)", "kubota-m7-2015", 2015, null,
                    E("V6108 121 KM", 121, 89, 6100, die), E("V6108 145 KM", 145, 107, 6100, die),
                    E("V6108 175 KM", 175, 129, 6100, die)) ]},
            new Model { BrandId = B("Kubota"), Name = "M5", Slug = "kubota-m5", Generations = [
                G("M5 (2016–)", "kubota-m5-2016", 2016, null,
                    E("V3800 95 KM", 95, 70, 3769, die), E("V3800 105 KM", 105, 77, 3769, die),
                    E("V3800 115 KM", 115, 85, 3769, die)) ]},
            new Model { BrandId = B("Kubota"), Name = "L-Series", Slug = "kubota-l-series", Generations = [
                G("L-Series (2010–)", "kubota-l-series-2010", 2010, null,
                    E("D1305 24 KM", 24, 18, 1261, die), E("D1803 37 KM", 37, 27, 1826, die),
                    E("V2403 52 KM", 52, 38, 2434, die), E("V3307 70 KM", 70, 51, 3318, die)) ]},
        ]);

        // ─── CATERPILLAR ──────────────────────────────────────────────────────────
        if (B("Caterpillar") > 0) models.AddRange([
            new Model { BrandId = B("Caterpillar"), Name = "320", Slug = "caterpillar-320", Generations = [
                G("320 (2019–)", "caterpillar-320-2019", 2019, null,
                    E("Cat C4.4 ACERT 121 KM", 121, 89, 4400, die)) ]},
            new Model { BrandId = B("Caterpillar"), Name = "336", Slug = "caterpillar-336", Generations = [
                G("336 (2019–)", "caterpillar-336-2019", 2019, null,
                    E("Cat C9.3B 265 KM", 265, 195, 9300, die)) ]},
            new Model { BrandId = B("Caterpillar"), Name = "966", Slug = "caterpillar-966", Generations = [
                G("966 (2017–)", "caterpillar-966-2017", 2017, null,
                    E("Cat C9.3B 263 KM", 263, 193, 9300, die)) ]},
            new Model { BrandId = B("Caterpillar"), Name = "D6", Slug = "caterpillar-d6", Generations = [
                G("D6 (2017–)", "caterpillar-d6-2017", 2017, null,
                    E("Cat C9.3B 215 KM", 215, 158, 9300, die)) ]},
        ]);

        // ─── JCB ──────────────────────────────────────────────────────────────────
        if (B("JCB") > 0) models.AddRange([
            new Model { BrandId = B("JCB"), Name = "3CX", Slug = "jcb-3cx", Generations = [
                G("3CX (2008–)", "jcb-3cx-2008", 2008, null,
                    E("JCB EcoMAX 109 KM", 109, 80, 4400, die)) ]},
            new Model { BrandId = B("JCB"), Name = "4CX", Slug = "jcb-4cx", Generations = [
                G("4CX (2008–)", "jcb-4cx-2008", 2008, null,
                    E("JCB EcoMAX 109 KM", 109, 80, 4400, die)) ]},
            new Model { BrandId = B("JCB"), Name = "JS220", Slug = "jcb-js220", Generations = [
                G("JS220 (2015–)", "jcb-js220-2015", 2015, null,
                    E("JCB EcoMAX 160 KM", 160, 118, 4400, die)) ]},
            new Model { BrandId = B("JCB"), Name = "540-180", Slug = "jcb-540-180", Generations = [
                G("540-180 (2016–)", "jcb-540-180-2016", 2016, null,
                    E("JCB EcoMAX 109 KM", 109, 80, 4400, die)) ]},
        ]);

        // ─── KOMATSU ──────────────────────────────────────────────────────────────
        if (B("Komatsu") > 0) models.AddRange([
            new Model { BrandId = B("Komatsu"), Name = "PC200", Slug = "komatsu-pc200", Generations = [
                G("PC200 (2012–)", "komatsu-pc200-2012", 2012, null,
                    E("SAA6D107E 112 KM", 112, 82, 6690, die)) ]},
            new Model { BrandId = B("Komatsu"), Name = "PC360", Slug = "komatsu-pc360", Generations = [
                G("PC360 (2012–)", "komatsu-pc360-2012", 2012, null,
                    E("SAA6D114E 258 KM", 258, 190, 8270, die)) ]},
            new Model { BrandId = B("Komatsu"), Name = "WA320", Slug = "komatsu-wa320", Generations = [
                G("WA320 (2013–)", "komatsu-wa320-2013", 2013, null,
                    E("SAA6D107E 150 KM", 150, 110, 6690, die)) ]},
        ]);

        // ─── LIEBHERR ─────────────────────────────────────────────────────────────
        if (B("Liebherr") > 0) models.AddRange([
            new Model { BrandId = B("Liebherr"), Name = "R 924", Slug = "liebherr-r-924", Generations = [
                G("R 924 (2018–)", "liebherr-r-924-2018", 2018, null,
                    E("Liebherr D934 156 KM", 156, 115, 6700, die)) ]},
            new Model { BrandId = B("Liebherr"), Name = "R 934", Slug = "liebherr-r-934", Generations = [
                G("R 934 (2018–)", "liebherr-r-934-2018", 2018, null,
                    E("Liebherr D946 215 KM", 215, 158, 9900, die)) ]},
            new Model { BrandId = B("Liebherr"), Name = "LTM 1030", Slug = "liebherr-ltm-1030", Generations = [
                G("LTM 1030 (2010–)", "liebherr-ltm-1030-2010", 2010, null,
                    E("Liebherr D924 174 KM", 174, 128, 6700, die)) ]},
        ]);

        // ─── BOBCAT ───────────────────────────────────────────────────────────────
        if (B("Bobcat") > 0) models.AddRange([
            new Model { BrandId = B("Bobcat"), Name = "E50", Slug = "bobcat-e50", Generations = [
                G("E50 (2014–)", "bobcat-e50-2014", 2014, null,
                    E("Kubota V2607 41 KM", 41, 30, 2615, die)) ]},
            new Model { BrandId = B("Bobcat"), Name = "T650", Slug = "bobcat-t650", Generations = [
                G("T650 (2012–)", "bobcat-t650-2012", 2012, null,
                    E("Bobcat/Doosan D24 74 KM", 74, 54, 2400, die)) ]},
        ]);

        // ─── TAKEUCHI ─────────────────────────────────────────────────────────────
        if (B("Takeuchi") > 0) models.AddRange([
            new Model { BrandId = B("Takeuchi"), Name = "TB216", Slug = "takeuchi-tb216", Generations = [
                G("TB216 (2018–)", "takeuchi-tb216-2018", 2018, null,
                    E("Yanmar 3TNV70 13 KM", 13, 10, 854, die)) ]},
            new Model { BrandId = B("Takeuchi"), Name = "TB260", Slug = "takeuchi-tb260", Generations = [
                G("TB260 (2015–)", "takeuchi-tb260-2015", 2015, null,
                    E("Yanmar 4TNV94 47 KM", 47, 35, 2196, die)) ]},
        ]);

        if (models.Count == 0) return;

        db.Models.AddRange(models);
        db.SaveChanges();
        logger.LogInformation("Seeded {Count} models with generations and engine versions", models.Count);
    }
}
