using CarsWebsite;

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
        static EngineVersion E(string name, int hp, int kw, int? disp, int fuelId) =>
            new() { EngineName = name, PowerHP = hp, PowerKW = kw, Displacement = disp, FuelTypeId = fuelId };

        // Helper: create Generation with engines
        static Generation G(string name, string slug, int yFrom, int? yTo, params EngineVersion[] engines) =>
            new() { Name = name, Slug = slug, YearFrom = yFrom, YearTo = yTo, EngineVersions = engines.ToList() };

        var models = new List<Model>();

        // ─── VOLKSWAGEN ───────────────────────────────────────────────────────────
        if (B("Volkswagen") > 0) models.AddRange([
            new Model { BrandId = B("Volkswagen"), Name = "Golf", Slug = "vw-golf", Generations = [
                G("Mk7 (2012–2019)", "vw-golf-mk7", 2012, 2019,
                    E("1.0 TSI 115 KM", 115, 85, 999, ben), E("1.4 TSI 125 KM", 125, 92, 1395, ben),
                    E("1.5 TSI 130 KM", 130, 96, 1498, ben), E("2.0 GTI TSI 220 KM", 220, 162, 1984, ben),
                    E("1.6 TDI 115 KM", 115, 85, 1598, die), E("2.0 TDI 150 KM", 150, 110, 1968, die)),
                G("Mk8 (2019–)", "vw-golf-mk8", 2019, null,
                    E("1.0 eTSI 110 KM", 110, 81, 999, mild), E("1.5 eTSI 150 KM", 150, 110, 1498, mild),
                    E("2.0 GTI TSI 245 KM", 245, 180, 1984, ben),
                    E("2.0 TDI 115 KM", 115, 85, 1968, die), E("2.0 TDI 150 KM", 150, 110, 1968, die),
                    E("GTE eHybrid 204 KM", 204, 150, 1395, phev)) ]},
            new Model { BrandId = B("Volkswagen"), Name = "Passat", Slug = "vw-passat", Generations = [
                G("B8 (2014–2023)", "vw-passat-b8", 2014, 2023,
                    E("1.5 TSI 150 KM", 150, 110, 1498, ben), E("2.0 TSI 220 KM", 220, 162, 1984, ben),
                    E("1.6 TDI 120 KM", 120, 88, 1598, die), E("2.0 TDI 150 KM", 150, 110, 1968, die),
                    E("2.0 TDI 190 KM", 190, 140, 1968, die), E("GTE 218 KM", 218, 160, 1395, phev)),
                G("B9 (2023–)", "vw-passat-b9", 2023, null,
                    E("1.5 eTSI 150 KM", 150, 110, 1498, mild), E("2.0 TSI 265 KM", 265, 195, 1984, ben),
                    E("2.0 TDI 150 KM", 150, 110, 1968, die), E("2.0 TDI 193 KM", 193, 142, 1968, die),
                    E("GTE 272 KM", 272, 200, 1498, phev)) ]},
            new Model { BrandId = B("Volkswagen"), Name = "Tiguan", Slug = "vw-tiguan", Generations = [
                G("Mk1 (2007–2016)", "vw-tiguan-mk1", 2007, 2016,
                    E("1.4 TSI 125 KM", 125, 92, 1390, ben), E("2.0 TSI 200 KM", 200, 147, 1984, ben),
                    E("2.0 TDI 110 KM", 110, 81, 1968, die), E("2.0 TDI 140 KM", 140, 103, 1968, die)),
                G("Mk2 (2016–)", "vw-tiguan-mk2", 2016, null,
                    E("1.5 TSI 130 KM", 130, 96, 1498, ben), E("2.0 TSI 190 KM", 190, 140, 1984, ben),
                    E("2.0 TSI 320 KM R", 320, 235, 1984, ben),
                    E("2.0 TDI 150 KM", 150, 110, 1968, die), E("2.0 TDI 200 KM", 200, 147, 1968, die),
                    E("eHybrid 245 KM", 245, 180, 1395, phev)) ]},
            new Model { BrandId = B("Volkswagen"), Name = "Polo", Slug = "vw-polo", Generations = [
                G("AW (2017–)", "vw-polo-aw", 2017, null,
                    E("1.0 MPI 65 KM", 65, 48, 999, ben), E("1.0 TSI 95 KM", 95, 70, 999, ben),
                    E("1.0 TSI 110 KM", 110, 81, 999, ben), E("2.0 TSI GTI 207 KM", 207, 152, 1984, ben),
                    E("1.6 TDI 80 KM", 80, 59, 1598, die), E("1.6 TDI 95 KM", 95, 70, 1598, die)) ]},
            new Model { BrandId = B("Volkswagen"), Name = "T-Roc", Slug = "vw-t-roc", Generations = [
                G("Mk1 (2017–)", "vw-t-roc-mk1", 2017, null,
                    E("1.0 TSI 115 KM", 115, 85, 999, ben), E("1.5 TSI 150 KM", 150, 110, 1498, ben),
                    E("2.0 TSI 300 KM R", 300, 221, 1984, ben),
                    E("1.6 TDI 115 KM", 115, 85, 1598, die), E("2.0 TDI 150 KM", 150, 110, 1968, die)) ]},
        ]);

        // ─── SKODA ────────────────────────────────────────────────────────────────
        if (B("Skoda") > 0) models.AddRange([
            new Model { BrandId = B("Skoda"), Name = "Octavia", Slug = "skoda-octavia", Generations = [
                G("III (2012–2020)", "skoda-octavia-iii", 2012, 2020,
                    E("1.0 TSI 115 KM", 115, 85, 999, ben), E("1.4 TSI 150 KM", 150, 110, 1395, ben),
                    E("2.0 TSI RS 230 KM", 230, 169, 1984, ben),
                    E("1.6 TDI 105 KM", 105, 77, 1598, die), E("2.0 TDI 150 KM", 150, 110, 1968, die),
                    E("2.0 TDI 184 KM", 184, 135, 1968, die)),
                G("IV (2020–)", "skoda-octavia-iv", 2020, null,
                    E("1.0 TSI 110 KM", 110, 81, 999, ben), E("1.5 TSI 150 KM", 150, 110, 1498, mild),
                    E("2.0 TSI RS 245 KM", 245, 180, 1984, ben),
                    E("2.0 TDI 115 KM", 115, 85, 1968, die), E("2.0 TDI 150 KM", 150, 110, 1968, die),
                    E("iV PHEV 245 KM", 245, 180, 1395, phev)) ]},
            new Model { BrandId = B("Skoda"), Name = "Fabia", Slug = "skoda-fabia", Generations = [
                G("III (2014–2021)", "skoda-fabia-iii", 2014, 2021,
                    E("1.0 MPI 60 KM", 60, 44, 999, ben), E("1.0 TSI 95 KM", 95, 70, 999, ben),
                    E("1.4 TSI 150 KM", 150, 110, 1395, ben), E("1.4 TDI 90 KM", 90, 66, 1422, die)),
                G("IV (2021–)", "skoda-fabia-iv", 2021, null,
                    E("1.0 MPI 65 KM", 65, 48, 999, ben), E("1.0 TSI 95 KM", 95, 70, 999, ben),
                    E("1.5 TSI 150 KM", 150, 110, 1498, ben)) ]},
            new Model { BrandId = B("Skoda"), Name = "Kodiaq", Slug = "skoda-kodiaq", Generations = [
                G("I (2016–2023)", "skoda-kodiaq-i", 2016, 2023,
                    E("1.5 TSI 150 KM", 150, 110, 1498, ben), E("2.0 TSI 180 KM", 180, 132, 1984, ben),
                    E("2.0 TSI RS 245 KM", 245, 180, 1984, ben),
                    E("2.0 TDI 150 KM", 150, 110, 1968, die), E("2.0 TDI 190 KM", 190, 140, 1968, die)),
                G("II (2023–)", "skoda-kodiaq-ii", 2023, null,
                    E("1.5 TSI 150 KM", 150, 110, 1498, mild), E("2.0 TSI 204 KM", 204, 150, 1984, ben),
                    E("2.0 TDI 150 KM", 150, 110, 1968, die), E("iV PHEV 204 KM", 204, 150, 1498, phev)) ]},
            new Model { BrandId = B("Skoda"), Name = "Superb", Slug = "skoda-superb", Generations = [
                G("III (2015–2023)", "skoda-superb-iii", 2015, 2023,
                    E("1.5 TSI 150 KM", 150, 110, 1498, ben), E("2.0 TSI 190 KM", 190, 140, 1984, ben),
                    E("2.0 TDI 150 KM", 150, 110, 1968, die), E("2.0 TDI 190 KM", 190, 140, 1968, die),
                    E("iV PHEV 218 KM", 218, 160, 1395, phev)) ]},
            new Model { BrandId = B("Skoda"), Name = "Kamiq", Slug = "skoda-kamiq", Generations = [
                G("I (2019–)", "skoda-kamiq-i", 2019, null,
                    E("1.0 TSI 95 KM", 95, 70, 999, ben), E("1.0 TSI 115 KM", 115, 85, 999, ben),
                    E("1.5 TSI 150 KM", 150, 110, 1498, ben)) ]},
        ]);

        // ─── AUDI ─────────────────────────────────────────────────────────────────
        if (B("Audi") > 0) models.AddRange([
            new Model { BrandId = B("Audi"), Name = "A3", Slug = "audi-a3", Generations = [
                G("8V (2012–2020)", "audi-a3-8v", 2012, 2020,
                    E("1.0 TFSI 116 KM", 116, 85, 999, ben), E("1.4 TFSI 150 KM", 150, 110, 1395, ben),
                    E("2.0 TFSI 190 KM", 190, 140, 1984, ben), E("2.0 TFSI S3 310 KM", 310, 228, 1984, ben),
                    E("1.6 TDI 116 KM", 116, 85, 1598, die), E("2.0 TDI 150 KM", 150, 110, 1968, die)),
                G("8Y (2020–)", "audi-a3-8y", 2020, null,
                    E("30 TFSI 110 KM", 110, 81, 999, mild), E("35 TFSI 150 KM", 150, 110, 1498, mild),
                    E("40 TFSI S3 310 KM", 310, 228, 1984, ben),
                    E("30 TDI 116 KM", 116, 85, 1968, die), E("35 TDI 150 KM", 150, 110, 1968, die),
                    E("45 TFSIe PHEV 204 KM", 204, 150, 1395, phev)) ]},
            new Model { BrandId = B("Audi"), Name = "A4", Slug = "audi-a4", Generations = [
                G("B8 (2007–2015)", "audi-a4-b8", 2007, 2015,
                    E("1.8 TFSI 160 KM", 160, 118, 1798, ben), E("2.0 TFSI 211 KM", 211, 155, 1984, ben),
                    E("3.0 TFSI 272 KM", 272, 200, 2995, ben),
                    E("2.0 TDI 143 KM", 143, 105, 1968, die), E("2.0 TDI 177 KM", 177, 130, 1968, die),
                    E("3.0 TDI 245 KM", 245, 180, 2967, die)),
                G("B9 (2015–)", "audi-a4-b9", 2015, null,
                    E("35 TFSI 150 KM", 150, 110, 1498, ben), E("40 TFSI 204 KM", 204, 150, 1984, ben),
                    E("45 TFSI 265 KM", 265, 195, 1984, ben), E("S4 TFSI 341 KM", 341, 251, 2995, ben),
                    E("30 TDI 136 KM", 136, 100, 1968, die), E("35 TDI 163 KM", 163, 120, 1968, die),
                    E("40 TDI 204 KM", 204, 150, 1968, die)) ]},
            new Model { BrandId = B("Audi"), Name = "A6", Slug = "audi-a6", Generations = [
                G("C7 (2011–2018)", "audi-a6-c7", 2011, 2018,
                    E("2.0 TFSI 252 KM", 252, 185, 1984, ben), E("3.0 TFSI 333 KM", 333, 245, 2995, ben),
                    E("S6 4.0 TFSI 420 KM", 420, 309, 3993, ben),
                    E("2.0 TDI 190 KM", 190, 140, 1968, die), E("3.0 TDI 218 KM", 218, 160, 2967, die),
                    E("3.0 TDI 272 KM", 272, 200, 2967, die)),
                G("C8 (2018–)", "audi-a6-c8", 2018, null,
                    E("40 TFSI 204 KM", 204, 150, 1984, mild), E("45 TFSI 265 KM", 265, 195, 1984, mild),
                    E("55 TFSI 340 KM", 340, 250, 2995, mild),
                    E("40 TDI 204 KM", 204, 150, 1968, mild), E("45 TDI 231 KM", 231, 170, 2967, mild),
                    E("55 TFSIe PHEV 367 KM", 367, 270, 2995, phev)) ]},
            new Model { BrandId = B("Audi"), Name = "Q5", Slug = "audi-q5", Generations = [
                G("8R (2008–2016)", "audi-q5-8r", 2008, 2016,
                    E("2.0 TFSI 225 KM", 225, 165, 1984, ben), E("3.0 TFSI 272 KM", 272, 200, 2995, ben),
                    E("2.0 TDI 150 KM", 150, 110, 1968, die), E("2.0 TDI 177 KM", 177, 130, 1968, die),
                    E("3.0 TDI 245 KM", 245, 180, 2967, die)),
                G("FY (2016–)", "audi-q5-fy", 2016, null,
                    E("40 TFSI 204 KM", 204, 150, 1984, mild), E("45 TFSI 265 KM", 265, 195, 1984, mild),
                    E("SQ5 TFSI 341 KM", 341, 251, 2995, ben),
                    E("35 TDI 163 KM", 163, 120, 1968, die), E("40 TDI 204 KM", 204, 150, 1968, die),
                    E("55 TFSIe PHEV 367 KM", 367, 270, 1984, phev)) ]},
            new Model { BrandId = B("Audi"), Name = "Q3", Slug = "audi-q3", Generations = [
                G("8U (2011–2018)", "audi-q3-8u", 2011, 2018,
                    E("1.4 TFSI 150 KM", 150, 110, 1395, ben), E("2.0 TFSI 211 KM", 211, 155, 1984, ben),
                    E("1.4 TDI 90 KM", 90, 66, 1422, die), E("2.0 TDI 150 KM", 150, 110, 1968, die)),
                G("F3 (2018–)", "audi-q3-f3", 2018, null,
                    E("35 TFSI 150 KM", 150, 110, 1498, ben), E("40 TFSI 190 KM", 190, 140, 1984, ben),
                    E("45 TFSI RS Q3 400 KM", 400, 294, 2480, ben),
                    E("35 TDI 150 KM", 150, 110, 1968, die)) ]},
        ]);

        // ─── BMW ──────────────────────────────────────────────────────────────────
        if (B("BMW") > 0) models.AddRange([
            new Model { BrandId = B("BMW"), Name = "Seria 3", Slug = "bmw-seria-3", Generations = [
                G("F30 (2011–2018)", "bmw-3-f30", 2011, 2018,
                    E("316i 136 KM", 136, 100, 1598, ben), E("318i 136 KM", 136, 100, 1499, ben),
                    E("320i 184 KM", 184, 135, 1998, ben), E("328i 245 KM", 245, 180, 1997, ben),
                    E("335i 306 KM", 306, 225, 2979, ben), E("M3 431 KM", 431, 317, 2979, ben),
                    E("316d 116 KM", 116, 85, 1995, die), E("318d 143 KM", 143, 105, 1995, die),
                    E("320d 190 KM", 190, 140, 1995, die), E("330d 258 KM", 258, 190, 2993, die)),
                G("G20 (2018–)", "bmw-3-g20", 2018, null,
                    E("318i 156 KM", 156, 115, 1499, mild), E("320i 184 KM", 184, 135, 1998, mild),
                    E("330i 258 KM", 258, 190, 1998, mild), E("M340i 374 KM", 374, 275, 2998, mild),
                    E("M3 Competition 510 KM", 510, 375, 2993, ben),
                    E("318d 150 KM", 150, 110, 1995, mild), E("320d 190 KM", 190, 140, 1995, mild),
                    E("330d 286 KM", 286, 210, 2993, mild),
                    E("330e PHEV 292 KM", 292, 215, 1998, phev)) ]},
            new Model { BrandId = B("BMW"), Name = "Seria 5", Slug = "bmw-seria-5", Generations = [
                G("F10 (2009–2016)", "bmw-5-f10", 2009, 2016,
                    E("520i 184 KM", 184, 135, 1997, ben), E("528i 245 KM", 245, 180, 1997, ben),
                    E("535i 306 KM", 306, 225, 2979, ben), E("M5 560 KM", 560, 412, 4395, ben),
                    E("520d 190 KM", 190, 140, 1995, die), E("530d 258 KM", 258, 190, 2993, die)),
                G("G30 (2016–)", "bmw-5-g30", 2016, null,
                    E("520i 184 KM", 184, 135, 1998, mild), E("530i 252 KM", 252, 185, 1998, mild),
                    E("540i 333 KM", 333, 245, 2998, mild), E("M5 Competition 625 KM", 625, 460, 4395, ben),
                    E("520d 190 KM", 190, 140, 1995, mild), E("530d 286 KM", 286, 210, 2993, mild),
                    E("530e PHEV 292 KM", 292, 215, 1998, phev)) ]},
            new Model { BrandId = B("BMW"), Name = "Seria 1", Slug = "bmw-seria-1", Generations = [
                G("F20 (2011–2019)", "bmw-1-f20", 2011, 2019,
                    E("116i 109 KM", 109, 80, 1499, ben), E("118i 136 KM", 136, 100, 1499, ben),
                    E("120i 184 KM", 184, 135, 1998, ben), E("M140i 340 KM", 340, 250, 2998, ben),
                    E("116d 116 KM", 116, 85, 1496, die), E("118d 150 KM", 150, 110, 1995, die)),
                G("F40 (2019–)", "bmw-1-f40", 2019, null,
                    E("116i 109 KM", 109, 80, 1499, mild), E("118i 136 KM", 136, 100, 1499, mild),
                    E("120i 178 KM", 178, 131, 1998, mild), E("M135i xDrive 306 KM", 306, 225, 1998, mild),
                    E("116d 116 KM", 116, 85, 1496, mild), E("118d 150 KM", 150, 110, 1995, mild)) ]},
            new Model { BrandId = B("BMW"), Name = "X3", Slug = "bmw-x3", Generations = [
                G("F25 (2010–2017)", "bmw-x3-f25", 2010, 2017,
                    E("xDrive20i 184 KM", 184, 135, 1997, ben), E("xDrive28i 245 KM", 245, 180, 1997, ben),
                    E("xDrive20d 184 KM", 184, 135, 1995, die), E("xDrive30d 258 KM", 258, 190, 2993, die)),
                G("G01 (2017–)", "bmw-x3-g01", 2017, null,
                    E("sDrive20i 184 KM", 184, 135, 1998, mild), E("xDrive30i 252 KM", 252, 185, 1998, mild),
                    E("M40i 360 KM", 360, 265, 2998, mild),
                    E("xDrive20d 190 KM", 190, 140, 1995, mild), E("xDrive30d 286 KM", 286, 210, 2993, mild),
                    E("30e PHEV 292 KM", 292, 215, 1998, phev)) ]},
            new Model { BrandId = B("BMW"), Name = "X5", Slug = "bmw-x5", Generations = [
                G("F15 (2013–2018)", "bmw-x5-f15", 2013, 2018,
                    E("xDrive35i 306 KM", 306, 225, 2979, ben), E("xDrive50i 450 KM", 450, 331, 4395, ben),
                    E("xDrive25d 218 KM", 218, 160, 1995, die), E("xDrive30d 258 KM", 258, 190, 2993, die),
                    E("xDrive40d 313 KM", 313, 230, 2993, die)),
                G("G05 (2018–)", "bmw-x5-g05", 2018, null,
                    E("xDrive40i 340 KM", 340, 250, 2998, mild), E("M60i 530 KM", 530, 390, 4395, mild),
                    E("xDrive30d 286 KM", 286, 210, 2993, mild), E("xDrive40d 340 KM", 340, 250, 2993, mild),
                    E("xDrive45e PHEV 394 KM", 394, 290, 2998, phev)) ]},
        ]);

        // ─── MERCEDES-BENZ ────────────────────────────────────────────────────────
        if (B("Mercedes-Benz") > 0) models.AddRange([
            new Model { BrandId = B("Mercedes-Benz"), Name = "Klasa C", Slug = "mb-klasa-c", Generations = [
                G("W204 (2007–2014)", "mb-c-w204", 2007, 2014,
                    E("C180 156 KM", 156, 115, 1796, ben), E("C200 184 KM", 184, 135, 1796, ben),
                    E("C250 204 KM", 204, 150, 1796, ben), E("C63 AMG 457 KM", 457, 336, 6208, ben),
                    E("C200d 136 KM", 136, 100, 2143, die), E("C220d 170 KM", 170, 125, 2143, die),
                    E("C250d 204 KM", 204, 150, 2143, die)),
                G("W205 (2014–2021)", "mb-c-w205", 2014, 2021,
                    E("C180 156 KM", 156, 115, 1595, ben), E("C200 184 KM", 184, 135, 1991, mild),
                    E("C300 258 KM", 258, 190, 1991, mild), E("C63 AMG 476 KM", 476, 350, 3982, ben),
                    E("C200d 160 KM", 160, 118, 1598, mild), E("C220d 194 KM", 194, 143, 1950, mild),
                    E("C300e PHEV 320 KM", 320, 235, 1991, phev)),
                G("W206 (2021–)", "mb-c-w206", 2021, null,
                    E("C180 170 KM", 170, 125, 1496, mild), E("C200 204 KM", 204, 150, 1496, mild),
                    E("C300 258 KM", 258, 190, 1999, mild), E("C63 AMG E 680 KM", 680, 500, 1999, phev),
                    E("C220d 200 KM", 200, 147, 1993, mild), E("C300e PHEV 313 KM", 313, 230, 1496, phev)) ]},
            new Model { BrandId = B("Mercedes-Benz"), Name = "Klasa E", Slug = "mb-klasa-e", Generations = [
                G("W213 (2016–2023)", "mb-e-w213", 2016, 2023,
                    E("E200 197 KM", 197, 145, 1991, mild), E("E300 258 KM", 258, 190, 1991, mild),
                    E("E450 367 KM", 367, 270, 2999, mild), E("E63 AMG S 612 KM", 612, 450, 3982, ben),
                    E("E200d 163 KM", 163, 120, 1950, mild), E("E220d 194 KM", 194, 143, 1950, mild),
                    E("E400d 340 KM", 340, 250, 2925, mild), E("E300e PHEV 320 KM", 320, 235, 1991, phev)) ]},
            new Model { BrandId = B("Mercedes-Benz"), Name = "Klasa A", Slug = "mb-klasa-a", Generations = [
                G("W176 (2012–2018)", "mb-a-w176", 2012, 2018,
                    E("A180 122 KM", 122, 90, 1595, ben), E("A200 156 KM", 156, 115, 1595, ben),
                    E("A250 211 KM", 211, 155, 1991, ben), E("A45 AMG 381 KM", 381, 280, 1991, ben),
                    E("A180d 109 KM", 109, 80, 1461, die), E("A200d 136 KM", 136, 100, 1461, die)),
                G("W177 (2018–)", "mb-a-w177", 2018, null,
                    E("A180 136 KM", 136, 100, 1332, mild), E("A200 163 KM", 163, 120, 1332, mild),
                    E("A250 224 KM", 224, 165, 1991, mild), E("A45 AMG S 421 KM", 421, 310, 1991, ben),
                    E("A180d 116 KM", 116, 85, 1461, die), E("A220d 190 KM", 190, 140, 1950, die),
                    E("A250e PHEV 218 KM", 218, 160, 1332, phev)) ]},
            new Model { BrandId = B("Mercedes-Benz"), Name = "GLC", Slug = "mb-glc", Generations = [
                G("X253 (2015–2022)", "mb-glc-x253", 2015, 2022,
                    E("GLC 200 197 KM", 197, 145, 1991, mild), E("GLC 300 258 KM", 258, 190, 1991, mild),
                    E("GLC 43 AMG 390 KM", 390, 287, 2996, mild),
                    E("GLC 200d 163 KM", 163, 120, 1950, mild), E("GLC 300d 245 KM", 245, 180, 1950, mild),
                    E("GLC 300e PHEV 320 KM", 320, 235, 1991, phev)),
                G("X254 (2022–)", "mb-glc-x254", 2022, null,
                    E("GLC 200 204 KM", 204, 150, 1496, mild), E("GLC 300 258 KM", 258, 190, 1999, mild),
                    E("GLC 43 AMG 421 KM", 421, 310, 2999, mild),
                    E("GLC 220d 197 KM", 197, 145, 1993, mild), E("GLC 300d 272 KM", 272, 200, 1993, mild),
                    E("GLC 300e PHEV 313 KM", 313, 230, 1496, phev)) ]},
            new Model { BrandId = B("Mercedes-Benz"), Name = "Actros", Slug = "mb-actros", Generations = [
                G("MP4 (2011–2018)", "mb-actros-mp4", 2011, 2018,
                    E("OM471 421 KM", 421, 309, 12799, die), E("OM471 476 KM", 476, 350, 12799, die),
                    E("OM471 510 KM", 510, 375, 12799, die), E("OM473 578 KM", 578, 425, 15600, die)),
                G("MP5 (2018–)", "mb-actros-mp5", 2018, null,
                    E("OM471 400 KM", 400, 294, 12799, die), E("OM471 449 KM", 449, 330, 12799, die),
                    E("OM471 476 KM", 476, 350, 12799, die), E("OM473 530 KM", 530, 390, 15600, die)) ]},
            new Model { BrandId = B("Mercedes-Benz"), Name = "Sprinter", Slug = "mb-sprinter", Generations = [
                G("W906 (2006–2018)", "mb-sprinter-w906", 2006, 2018,
                    E("211 CDI 109 KM", 109, 80, 2143, die), E("213 CDI 129 KM", 129, 95, 2143, die),
                    E("316 CDI 163 KM", 163, 120, 2143, die), E("319 CDI 190 KM", 190, 140, 2143, die)),
                G("W907 (2018–)", "mb-sprinter-w907", 2018, null,
                    E("211 CDI 114 KM", 114, 84, 1950, die), E("214 CDI 143 KM", 143, 105, 1950, die),
                    E("316 CDI 163 KM", 163, 120, 1950, die), E("319 CDI 190 KM", 190, 140, 1950, die),
                    E("eSprintersElektryczny 115 KM", 115, 85, null, ev)) ]},
        ]);

        // ─── TOYOTA ───────────────────────────────────────────────────────────────
        if (B("Toyota") > 0) models.AddRange([
            new Model { BrandId = B("Toyota"), Name = "Corolla", Slug = "toyota-corolla", Generations = [
                G("E210 (2018–)", "toyota-corolla-e210", 2018, null,
                    E("1.2 T 116 KM", 116, 85, 1197, ben), E("2.0 GR Sport 261 KM", 261, 192, 1987, ben),
                    E("1.8 HEV 122 KM", 122, 90, 1798, hyb), E("2.0 HEV 196 KM", 196, 144, 1987, hyb),
                    E("2.0 PHEV 223 KM", 223, 164, 1987, phev)) ]},
            new Model { BrandId = B("Toyota"), Name = "Yaris", Slug = "toyota-yaris", Generations = [
                G("XP150 (2011–2019)", "toyota-yaris-xp150", 2011, 2019,
                    E("1.0 VVT-i 69 KM", 69, 51, 998, ben), E("1.33 VVT-i 99 KM", 99, 73, 1329, ben),
                    E("1.4 D-4D 90 KM", 90, 66, 1364, die), E("1.5 HEV 100 KM", 100, 74, 1497, hyb)),
                G("XP210 (2019–)", "toyota-yaris-xp210", 2019, null,
                    E("1.5 HEV 116 KM", 116, 85, 1490, hyb), E("GR Yaris 1.6 261 KM", 261, 192, 1618, ben)) ]},
            new Model { BrandId = B("Toyota"), Name = "RAV4", Slug = "toyota-rav4", Generations = [
                G("IV (2012–2018)", "toyota-rav4-iv", 2012, 2018,
                    E("2.0 VVT-i 152 KM", 152, 112, 1987, ben), E("2.5 HEV 197 KM", 197, 145, 2494, hyb),
                    E("2.0 D-4D 124 KM", 124, 91, 1998, die), E("2.2 D-4D 150 KM", 150, 110, 2231, die)),
                G("V (2018–)", "toyota-rav4-v", 2018, null,
                    E("2.0 VVT-i 175 KM", 175, 129, 1987, ben), E("2.5 HEV 218 KM", 218, 160, 2487, hyb),
                    E("2.5 PHEV 306 KM", 306, 225, 2487, phev)) ]},
            new Model { BrandId = B("Toyota"), Name = "Camry", Slug = "toyota-camry", Generations = [
                G("XV70 (2017–)", "toyota-camry-xv70", 2017, null,
                    E("2.0 VVT-i 150 KM", 150, 110, 1987, ben), E("2.5 HEV 218 KM", 218, 160, 2487, hyb)) ]},
            new Model { BrandId = B("Toyota"), Name = "C-HR", Slug = "toyota-chr", Generations = [
                G("X10 (2016–2023)", "toyota-chr-x10", 2016, 2023,
                    E("1.2 T 116 KM", 116, 85, 1197, ben), E("1.8 HEV 122 KM", 122, 90, 1798, hyb),
                    E("2.0 HEV 184 KM", 184, 135, 1987, hyb)) ]},
        ]);

        // ─── FORD ─────────────────────────────────────────────────────────────────
        if (B("Ford") > 0) models.AddRange([
            new Model { BrandId = B("Ford"), Name = "Focus", Slug = "ford-focus", Generations = [
                G("Mk3 (2011–2018)", "ford-focus-mk3", 2011, 2018,
                    E("1.0 EcoBoost 100 KM", 100, 74, 999, ben), E("1.0 EcoBoost 125 KM", 125, 92, 999, ben),
                    E("1.5 EcoBoost 150 KM", 150, 110, 1499, ben), E("2.0 ST 250 KM", 250, 184, 1999, ben),
                    E("1.5 TDCi 95 KM", 95, 70, 1499, die), E("1.5 TDCi 120 KM", 120, 88, 1499, die),
                    E("2.0 TDCi 150 KM", 150, 110, 1997, die), E("RS 2.3 350 KM", 350, 257, 2261, ben)),
                G("Mk4 (2018–)", "ford-focus-mk4", 2018, null,
                    E("1.0 EcoBoost 100 KM", 100, 74, 999, mild), E("1.0 EcoBoost 125 KM", 125, 92, 999, mild),
                    E("1.5 EcoBoost 150 KM", 150, 110, 1499, mild), E("2.3 ST 280 KM", 280, 206, 2261, ben),
                    E("1.5 EcoBlue 95 KM", 95, 70, 1499, die), E("1.5 EcoBlue 120 KM", 120, 88, 1499, die),
                    E("2.0 EcoBlue 150 KM", 150, 110, 1997, die)) ]},
            new Model { BrandId = B("Ford"), Name = "Fiesta", Slug = "ford-fiesta", Generations = [
                G("Mk7 (2008–2017)", "ford-fiesta-mk7", 2008, 2017,
                    E("1.0 EcoBoost 100 KM", 100, 74, 999, ben), E("1.25 82 KM", 82, 60, 1242, ben),
                    E("1.6 ST 182 KM", 182, 134, 1596, ben), E("1.4 TDCi 70 KM", 70, 51, 1399, die)),
                G("Mk8 (2017–)", "ford-fiesta-mk8", 2017, null,
                    E("1.0 EcoBoost 95 KM", 95, 70, 999, ben), E("1.0 EcoBoost 125 KM", 125, 92, 999, ben),
                    E("1.5 ST 200 KM", 200, 147, 1499, ben), E("1.5 TDCi 85 KM", 85, 63, 1499, die)) ]},
            new Model { BrandId = B("Ford"), Name = "Kuga", Slug = "ford-kuga", Generations = [
                G("Mk2 (2012–2019)", "ford-kuga-mk2", 2012, 2019,
                    E("1.5 EcoBoost 150 KM", 150, 110, 1499, ben), E("2.0 EcoBoost 242 KM", 242, 178, 1999, ben),
                    E("1.5 TDCi 120 KM", 120, 88, 1499, die), E("2.0 TDCi 150 KM", 150, 110, 1997, die)),
                G("Mk3 (2019–)", "ford-kuga-mk3", 2019, null,
                    E("1.5 EcoBoost 150 KM", 150, 110, 1499, mild), E("2.5 PHEV 225 KM", 225, 165, 2488, phev),
                    E("1.5 EcoBlue 120 KM", 120, 88, 1499, die), E("2.0 EcoBlue 150 KM", 150, 110, 1997, die)) ]},
            new Model { BrandId = B("Ford"), Name = "Mustang", Slug = "ford-mustang", Generations = [
                G("S550 (2014–2022)", "ford-mustang-s550", 2014, 2022,
                    E("2.3 EcoBoost 317 KM", 317, 233, 2261, ben), E("5.0 V8 GT 450 KM", 450, 331, 4951, ben),
                    E("5.2 V8 GT500 760 KM", 760, 559, 5163, ben)),
                G("S650 (2023–)", "ford-mustang-s650", 2023, null,
                    E("2.3 EcoBoost 317 KM", 317, 233, 2261, ben), E("5.0 V8 GT 450 KM", 450, 331, 4951, ben)) ]},
        ]);

        // ─── OPEL ─────────────────────────────────────────────────────────────────
        if (B("Opel") > 0) models.AddRange([
            new Model { BrandId = B("Opel"), Name = "Astra", Slug = "opel-astra", Generations = [
                G("K (2015–2021)", "opel-astra-k", 2015, 2021,
                    E("1.0 Turbo 105 KM", 105, 77, 999, ben), E("1.4 Turbo 125 KM", 125, 92, 1399, ben),
                    E("1.4 Turbo 150 KM", 150, 110, 1399, ben), E("OPC 280 KM", 280, 206, 1998, ben),
                    E("1.6 CDTi 110 KM", 110, 81, 1598, die), E("1.6 CDTi 136 KM", 136, 100, 1598, die)),
                G("L (2021–)", "opel-astra-l", 2021, null,
                    E("1.2 Turbo 110 KM", 110, 81, 1199, ben), E("1.2 Turbo 130 KM", 130, 96, 1199, ben),
                    E("1.6 Hybrid 180 KM", 180, 132, 1598, hyb),
                    E("1.5 Diesel 130 KM", 130, 96, 1499, die), E("PHEV 225 KM", 225, 165, 1598, phev)) ]},
            new Model { BrandId = B("Opel"), Name = "Insignia", Slug = "opel-insignia", Generations = [
                G("B (2017–)", "opel-insignia-b", 2017, null,
                    E("1.5 Turbo 140 KM", 140, 103, 1490, ben), E("2.0 Turbo 200 KM", 200, 147, 1998, ben),
                    E("GSi 260 KM", 260, 191, 1998, ben),
                    E("1.6 CDTi 136 KM", 136, 100, 1598, die), E("2.0 CDTi 170 KM", 170, 125, 1997, die)) ]},
            new Model { BrandId = B("Opel"), Name = "Corsa", Slug = "opel-corsa", Generations = [
                G("E (2014–2019)", "opel-corsa-e", 2014, 2019,
                    E("1.0 Turbo 90 KM", 90, 66, 999, ben), E("1.0 Turbo 115 KM", 115, 85, 999, ben),
                    E("1.4 Turbo 100 KM", 100, 74, 1399, ben), E("OPC 207 KM", 207, 152, 1598, ben),
                    E("1.3 CDTi 75 KM", 75, 55, 1248, die), E("1.3 CDTi 95 KM", 95, 70, 1248, die)),
                G("F (2019–)", "opel-corsa-f", 2019, null,
                    E("1.2 75 KM", 75, 55, 1199, ben), E("1.2 Turbo 100 KM", 100, 74, 1199, ben),
                    E("1.2 Turbo 130 KM", 130, 96, 1199, ben), E("Corsa-e Elektryczny 136 KM", 136, 100, null, ev)) ]},
        ]);

        // ─── RENAULT ──────────────────────────────────────────────────────────────
        if (B("Renault") > 0) models.AddRange([
            new Model { BrandId = B("Renault"), Name = "Megane", Slug = "renault-megane", Generations = [
                G("IV (2015–)", "renault-megane-iv", 2015, null,
                    E("1.3 TCe 115 KM", 115, 85, 1332, ben), E("1.3 TCe 140 KM", 140, 103, 1332, ben),
                    E("1.8 TCe RS 300 KM", 300, 221, 1798, ben),
                    E("1.5 dCi 90 KM", 90, 66, 1461, die), E("1.5 dCi 115 KM", 115, 85, 1461, die),
                    E("E-Tech PHEV 160 KM", 160, 118, 1618, phev)) ]},
            new Model { BrandId = B("Renault"), Name = "Clio", Slug = "renault-clio", Generations = [
                G("IV (2012–2019)", "renault-clio-iv", 2012, 2019,
                    E("0.9 TCe 75 KM", 75, 55, 898, ben), E("0.9 TCe 90 KM", 90, 66, 898, ben),
                    E("1.2 TCe 120 KM", 120, 88, 1197, ben), E("RS 200 KM", 200, 147, 1618, ben),
                    E("1.5 dCi 75 KM", 75, 55, 1461, die), E("1.5 dCi 90 KM", 90, 66, 1461, die)),
                G("V (2019–)", "renault-clio-v", 2019, null,
                    E("1.0 TCe 65 KM", 65, 48, 999, ben), E("1.0 TCe 90 KM", 90, 66, 999, ben),
                    E("1.3 TCe 130 KM", 130, 96, 1332, ben), E("E-Tech HEV 140 KM", 140, 103, 1598, hyb),
                    E("1.5 dCi 85 KM", 85, 63, 1461, die)) ]},
            new Model { BrandId = B("Renault"), Name = "Duster", Slug = "renault-duster", Generations = [
                G("I (2010–2017)", "renault-duster-i", 2010, 2017,
                    E("1.2 TCe 125 KM", 125, 92, 1197, ben), E("1.6 16V 105 KM", 105, 77, 1598, ben),
                    E("1.5 dCi 90 KM", 90, 66, 1461, die), E("1.5 dCi 110 KM", 110, 81, 1461, die)),
                G("II (2017–)", "renault-duster-ii", 2017, null,
                    E("1.0 TCe 100 KM", 100, 74, 999, ben), E("1.3 TCe 150 KM", 150, 110, 1332, ben),
                    E("1.5 dCi 90 KM", 90, 66, 1461, die), E("1.5 dCi 115 KM", 115, 85, 1461, die)) ]},
        ]);

        // ─── HYUNDAI ──────────────────────────────────────────────────────────────
        if (B("Hyundai") > 0) models.AddRange([
            new Model { BrandId = B("Hyundai"), Name = "i30", Slug = "hyundai-i30", Generations = [
                G("PD (2016–)", "hyundai-i30-pd", 2016, null,
                    E("1.0 T-GDI 100 KM", 100, 74, 998, ben), E("1.0 T-GDI 120 KM", 120, 88, 998, ben),
                    E("1.4 T-GDI 140 KM", 140, 103, 1353, ben), E("N 275 KM", 275, 202, 1998, ben),
                    E("1.4 CRDi 90 KM", 90, 66, 1396, die), E("1.6 CRDi 115 KM", 115, 85, 1582, die)) ]},
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

        if (models.Count == 0) return;

        db.Models.AddRange(models);
        db.SaveChanges();
        logger.LogInformation("Seeded {Count} models with generations and engine versions", models.Count);
    }
}
