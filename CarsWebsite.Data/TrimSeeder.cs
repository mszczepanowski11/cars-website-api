using CarsWebsite;
using cars_website_api.CarsWebsite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Data;

/// <summary>
/// Seeds real Polish-market trim/version data:
/// Brand → Model → Generation → Trim → (optional engine link).
///
/// Idempotent: skips if any Trim already exists.
/// For each generation we create named trims (equipment levels + performance variants).
/// Performance trims also update TrimId on the matching EngineVersion.
/// </summary>
public static class TrimSeeder
{
    public static void SeedTrims(AppDbContext db, ILogger logger)
    {
        if (db.Trims.Any())
        {
            logger.LogInformation("[TrimSeeder] Already seeded — skipping.");
            return;
        }

        var genDict = db.Generations
            .Where(g => g.Slug != null)
            .ToDictionary(g => g.Slug!, g => g.Id);

        if (!genDict.Any())
        {
            logger.LogWarning("[TrimSeeder] No generations found — skipping.");
            return;
        }

        int G(string slug) => genDict.TryGetValue(slug, out var id) ? id : 0;

        // --- Build all Trim records ---
        var trimDefs = new List<(string genSlug, string name, string? desc)>
        {
            // ── VW Golf Mk8 ──────────────────────────────────────────────────────
            ("vw-golf-mk8", "Life",           "Wersja bazowa"),
            ("vw-golf-mk8", "Style",          "Wersja komfortowa"),
            ("vw-golf-mk8", "R-Line",         "Pakiet sportowy R-Line"),
            ("vw-golf-mk8", "GTI",            "Golf GTI – hot-hatch 245 KM"),
            ("vw-golf-mk8", "GTI Clubsport",  "Golf GTI Clubsport – 300 KM"),
            ("vw-golf-mk8", "GTE",            "Golf GTE – hybryda plug-in"),
            ("vw-golf-mk8", "R",              "Golf R – 4Motion 320 KM"),

            // ── VW Polo AW ───────────────────────────────────────────────────────
            ("vw-polo-aw", "Trendline",  "Wersja bazowa"),
            ("vw-polo-aw", "Comfortline","Wersja komfortowa"),
            ("vw-polo-aw", "Highline",   "Wersja premium"),
            ("vw-polo-aw", "GTI",        "Polo GTI – hot-hatch 207 KM"),

            // ── VW Tiguan Mk2 ────────────────────────────────────────────────────
            ("vw-tiguan-mk2", "Life",     "Wersja bazowa"),
            ("vw-tiguan-mk2", "Elegance", "Wersja komfortowa"),
            ("vw-tiguan-mk2", "R-Line",   "Pakiet sportowy"),
            ("vw-tiguan-mk2", "R",        "Tiguan R – 4Motion 320 KM"),

            // ── VW Passat B9 ─────────────────────────────────────────────────────
            ("vw-passat-b9", "Business", "Wersja biznesowa"),
            ("vw-passat-b9", "Elegance", "Wersja komfortowa"),
            ("vw-passat-b9", "R-Line",   "Pakiet sportowy"),

            // ── Skoda Octavia IV ─────────────────────────────────────────────────
            ("skoda-octavia-iv", "Active",   "Wersja bazowa"),
            ("skoda-octavia-iv", "Ambition", "Wersja komfortowa"),
            ("skoda-octavia-iv", "Style",    "Wersja premium"),
            ("skoda-octavia-iv", "RS",       "Octavia RS – 245 KM"),
            ("skoda-octavia-iv", "iV",       "Octavia iV – hybryda PHEV"),

            // ── Skoda Fabia IV ───────────────────────────────────────────────────
            ("skoda-fabia-iv", "Active",   "Wersja bazowa"),
            ("skoda-fabia-iv", "Ambition", "Wersja komfortowa"),
            ("skoda-fabia-iv", "Style",    "Wersja premium"),

            // ── Skoda Kodiaq II ──────────────────────────────────────────────────
            ("skoda-kodiaq-ii", "Selection", "Wersja standardowa"),
            ("skoda-kodiaq-ii", "Sportline", "Pakiet sportowy"),
            ("skoda-kodiaq-ii", "L&K",       "Laurin & Klement – wersja premium"),

            // ── Audi A3 8Y ───────────────────────────────────────────────────────
            ("audi-a3-8y", "Advanced", "Wersja standardowa"),
            ("audi-a3-8y", "S line",   "Pakiet sportowy S line"),
            ("audi-a3-8y", "S3",       "Audi S3 – 310 KM quattro"),

            // ── Audi A4 B9 ───────────────────────────────────────────────────────
            ("audi-a4-b9", "Advanced", "Wersja standardowa"),
            ("audi-a4-b9", "S line",   "Pakiet sportowy S line"),
            ("audi-a4-b9", "S4",       "Audi S4 – 341 KM quattro"),

            // ── Audi Q5 FY ───────────────────────────────────────────────────────
            ("audi-q5-fy", "Sport",  "Wersja standardowa"),
            ("audi-q5-fy", "S line", "Pakiet sportowy S line"),
            ("audi-q5-fy", "SQ5",    "Audi SQ5 – sportowy SUV"),

            // ── BMW Seria 3 G20 ──────────────────────────────────────────────────
            ("bmw-3-g20", "Advantage",   "Wersja bazowa"),
            ("bmw-3-g20", "Sport Line",  "Pakiet sportowy"),
            ("bmw-3-g20", "M Sport",     "Pakiet M Sport"),
            ("bmw-3-g20", "M340i",       "BMW M340i – 374 KM xDrive"),
            ("bmw-3-g20", "M3",          "BMW M3 Competition – 510 KM"),

            // ── BMW Seria 5 G30 ──────────────────────────────────────────────────
            ("bmw-5-g30", "Advantage",   "Wersja bazowa"),
            ("bmw-5-g30", "Sport Line",  "Pakiet sportowy"),
            ("bmw-5-g30", "M Sport",     "Pakiet M Sport"),
            ("bmw-5-g30", "M5 Competition", "BMW M5 Competition – 625 KM"),

            // ── BMW Seria 1 F40 ──────────────────────────────────────────────────
            ("bmw-1-f40", "Advantage",  "Wersja bazowa"),
            ("bmw-1-f40", "Sport Line", "Pakiet sportowy"),
            ("bmw-1-f40", "M Sport",    "Pakiet M Sport"),
            ("bmw-1-f40", "M135i",      "BMW M135i xDrive – 306 KM"),

            // ── Mercedes Klasa C W206 ────────────────────────────────────────────
            ("mb-c-w206", "Avantgarde", "Wersja standardowa"),
            ("mb-c-w206", "AMG Line",   "Pakiet AMG Line"),
            ("mb-c-w206", "AMG C43",    "AMG C43 – sportowy 408 KM"),
            ("mb-c-w206", "AMG C63 E",  "AMG C63 E Performance – 680 KM PHEV"),

            // ── Mercedes Klasa A W177 ────────────────────────────────────────────
            ("mb-a-w177", "Progressive", "Wersja standardowa"),
            ("mb-a-w177", "AMG Line",    "Pakiet AMG Line"),
            ("mb-a-w177", "AMG A35",     "AMG A35 – 306 KM 4MATIC"),
            ("mb-a-w177", "AMG A45 S",   "AMG A45 S – 421 KM 4MATIC+"),

            // ── Mercedes GLC X254 ────────────────────────────────────────────────
            ("mb-glc-x254", "Avantgarde", "Wersja standardowa"),
            ("mb-glc-x254", "AMG Line",   "Pakiet AMG Line"),
            ("mb-glc-x254", "AMG GLC 43", "AMG GLC 43 – 421 KM 4MATIC+"),

            // ── Toyota Corolla E210 ──────────────────────────────────────────────
            ("toyota-corolla-e210", "Active",    "Wersja bazowa"),
            ("toyota-corolla-e210", "Comfort",   "Wersja komfortowa"),
            ("toyota-corolla-e210", "Executive", "Wersja premium"),
            ("toyota-corolla-e210", "GR Sport",  "Corolla GR Sport – 261 KM"),

            // ── Toyota Yaris XP210 ───────────────────────────────────────────────
            ("toyota-yaris-xp210", "Active",   "Wersja bazowa"),
            ("toyota-yaris-xp210", "Comfort",  "Wersja komfortowa"),
            ("toyota-yaris-xp210", "GR Yaris", "Toyota GR Yaris – 261 KM AWD"),

            // ── Ford Focus Mk4 ───────────────────────────────────────────────────
            ("ford-focus-mk4", "Trend",     "Wersja bazowa"),
            ("ford-focus-mk4", "Connected", "Wersja z łącznością"),
            ("ford-focus-mk4", "ST-Line",   "Pakiet sportowy ST-Line"),
            ("ford-focus-mk4", "Active",    "Wersja crossoverowa"),
            ("ford-focus-mk4", "ST",        "Ford Focus ST – 280 KM"),

            // ── Opel Astra L ─────────────────────────────────────────────────────
            ("opel-astra-l", "Edition",           "Wersja bazowa"),
            ("opel-astra-l", "Business Elegance", "Wersja biznesowa"),
            ("opel-astra-l", "GS",                "Pakiet sportowy GS"),

            // ── Seat Leon KL8 ────────────────────────────────────────────────────
            ("seat-leon-kl8", "Style",     "Wersja standardowa"),
            ("seat-leon-kl8", "FR",        "Seat Leon FR – sportowy 190 KM"),
            ("seat-leon-kl8", "e-Hybrid",  "Seat Leon e-Hybrid – PHEV 204 KM"),

            // ── Hyundai i30 PD ───────────────────────────────────────────────────
            ("hyundai-i30-pd", "Smart",    "Wersja bazowa"),
            ("hyundai-i30-pd", "Comfort",  "Wersja komfortowa"),
            ("hyundai-i30-pd", "Elegance", "Wersja premium"),
            ("hyundai-i30-pd", "N Line",   "Pakiet sportowy N Line"),
            ("hyundai-i30-pd", "N",        "Hyundai i30 N – hot-hatch 275 KM"),

            // ── Kia Ceed III ─────────────────────────────────────────────────────
            ("kia-ceed-iii", "L",       "Wersja bazowa"),
            ("kia-ceed-iii", "M",       "Wersja standardowa"),
            ("kia-ceed-iii", "GT Line", "Pakiet sportowy GT Line"),
            ("kia-ceed-iii", "GT",      "Kia Ceed GT – 204 KM"),

            // ── Honda Civic XI ───────────────────────────────────────────────────
            ("honda-civic-xi", "Advance",  "Wersja standardowa"),
            ("honda-civic-xi", "Sport",    "Wersja sportowa"),
            ("honda-civic-xi", "Type R",   "Honda Civic Type R – 329 KM"),

            // ── Renault Clio V ───────────────────────────────────────────────────
            ("renault-clio-v", "Equilibre",    "Wersja bazowa"),
            ("renault-clio-v", "Techno",       "Wersja technologiczna"),
            ("renault-clio-v", "Esprit Alpine","Pakiet Alpine"),
            ("renault-clio-v", "E-Tech",       "Clio E-Tech – hybryda"),
        };

        // Map: genSlug:trimName → engine name to link (null = no engine linkage)
        var engineLinks = new Dictionary<string, string>
        {
            ["vw-golf-mk8:GTI"]            = "2.0 GTI TSI 245 KM",
            ["vw-golf-mk8:GTE"]            = "GTE eHybrid 204 KM",
            ["vw-polo-aw:GTI"]             = "2.0 TSI GTI 207 KM",
            ["vw-tiguan-mk2:R"]            = "2.0 TSI 320 KM R",
            ["skoda-octavia-iv:RS"]        = "2.0 TSI RS 245 KM",
            ["skoda-octavia-iv:iV"]        = "iV PHEV 245 KM",
            ["audi-a3-8y:S3"]              = "40 TFSI S3 310 KM",
            ["audi-a4-b9:S4"]              = "S4 TFSI 341 KM",
            ["bmw-3-g20:M340i"]            = "M340i 374 KM",
            ["bmw-3-g20:M3"]               = "M3 Competition 510 KM",
            ["bmw-5-g30:M5 Competition"]   = "M5 Competition 625 KM",
            ["bmw-1-f40:M135i"]            = "M135i xDrive 306 KM",
            ["mb-c-w206:AMG C63 E"]        = "C63 AMG E 680 KM",
            ["mb-a-w177:AMG A45 S"]        = "A45 AMG S 421 KM",
            ["mb-glc-x254:AMG GLC 43"]     = "GLC 43 AMG 421 KM",
            ["toyota-corolla-e210:GR Sport"] = "2.0 GR Sport 261 KM",
            ["toyota-yaris-xp210:GR Yaris"]  = "GR Yaris 1.6 261 KM",
            ["ford-focus-mk4:ST"]            = "2.3 ST 280 KM",
            ["hyundai-i30-pd:N"]             = "N 275 KM",
            ["kia-ceed-iii:GT"]              = "GT 1.6 T-GDI 204 KM",
            ["honda-civic-xi:Type R"]        = "Type R 2.0 329 KM",
            ["seat-leon-kl8:FR"]             = "2.0 TSI FR 190 KM",
            ["seat-leon-kl8:e-Hybrid"]       = "e-Hybrid PHEV 204 KM",
        };

        // Create Trim records (skip if generation not in DB)
        var trimsToAdd = new List<Trim>();
        foreach (var (slug, name, desc) in trimDefs)
        {
            int genId = G(slug);
            if (genId == 0) continue;
            trimsToAdd.Add(new Trim { GenerationId = genId, Name = name, Description = desc });
        }

        if (trimsToAdd.Count == 0)
        {
            logger.LogWarning("[TrimSeeder] No matching generations found — skipping.");
            return;
        }

        db.Trims.AddRange(trimsToAdd);
        db.SaveChanges();
        logger.LogInformation("[TrimSeeder] Seeded {Count} trims", trimsToAdd.Count);

        // Link performance engines to their specific trims
        var savedTrims = db.Trims
            .Include(t => t.Generation)
            .Where(t => t.Generation.Slug != null)
            .ToList()
            .ToDictionary(t => $"{t.Generation.Slug}:{t.Name}", t => new { t.Id, t.GenerationId });

        int linked = 0;
        foreach (var (key, engineName) in engineLinks)
        {
            if (!savedTrims.TryGetValue(key, out var trimInfo)) continue;

            var engine = db.EngineVersions
                .FirstOrDefault(e => e.GenerationId == trimInfo.GenerationId
                                  && e.EngineName == engineName
                                  && e.TrimId == null);
            if (engine == null) continue;

            engine.TrimId = trimInfo.Id;
            db.SaveChanges();
            linked++;
        }

        logger.LogInformation("[TrimSeeder] Linked {Count} engines to trims", linked);
    }
}
