using System.Text.Json;
using CarsWebsite;
using cars_website_api.CarsWebsite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Data;

// Taksonomia Etap 5: fills the gaps the taxonomy audit found, additively.
//
// Every routine here follows the same contract, because getting this wrong is exactly what
// produced 863 duplicate models before: look the row up by a NORMALIZED natural key, insert only
// when genuinely absent, never overwrite an existing row. Running this twice must change nothing
// the second time.
//
// Deliberately NOT in Program.cs (already ~4700 lines, flagged by the audit) and deliberately NOT
// touching anything that would require merging existing categories - those are irreversible
// re-pointings of live adverts and are held back pending a product decision.
public static class TaxonomyGapSeeder
{
    private static string NKey(string? s) => (s ?? string.Empty).Trim().ToLowerInvariant();

    public static void Seed(AppDbContext db, ILogger logger)
    {
        logger.LogWarning("[STARTUP-TRACE] TaxonomyGapSeeder entered");
        try
        {
            SeedMissingBrands(db, logger);
            SeedAccessoryAndServiceSubtypes(db, logger);
            SeedMissingCategoryAttributes(db, logger);
            SeedMissingPartCategories(db, logger);
        }
        catch (Exception ex)
        {
            // Never block startup: this is data enrichment, not schema.
            logger.LogError(ex, "[TaxonomyGap] Seeding failed: {Msg}", ex.Message);
        }
    }

    // ── Marki ────────────────────────────────────────────────────────────────────────────────
    // The audit found the catalogue missing every recent Chinese make, nearly every EV-only make
    // and both Indian makes - the exact groups called out as mandatory for a marketplace going
    // global. OriginCountry/IsLuxury are filled in because the "samochody chińskie/amerykańskie"
    // and "samochody luksusowe" filters read them straight off the brand row.
    private static void SeedMissingBrands(AppDbContext db, ILogger logger)
    {
        var carCat = db.VehicleCategories.FirstOrDefault(c => c.Slug == "auta-osobowe");
        if (carCat is null)
        {
            logger.LogWarning("[TaxonomyGap] Category 'auta-osobowe' missing - skipping brand gap seeding");
            return;
        }

        // (Name, Slug, OriginCountry, IsLuxury)
        var wanted = new (string Name, string Slug, string Country, bool Luxury)[]
        {
            // Chiny - najwieksza luka; wszystkie obecne na rynku europejskim
            ("BYD",        "byd",        "Chiny", false),
            ("NIO",        "nio",        "Chiny", true),
            ("Xpeng",      "xpeng",      "Chiny", false),
            ("Chery",      "chery",      "Chiny", false),
            ("Geely",      "geely",      "Chiny", false),
            ("Haval",      "haval",      "Chiny", false),
            ("Omoda",      "omoda",      "Chiny", false),
            ("Jaecoo",     "jaecoo",     "Chiny", false),
            ("Leapmotor",  "leapmotor",  "Chiny", false),
            ("Zeekr",      "zeekr",      "Chiny", true),
            ("Dongfeng",   "dongfeng",   "Chiny", false),
            ("Lynk & Co",  "lynk-co",    "Chiny", false),
            ("Hongqi",     "hongqi",     "Chiny", true),
            ("Wuling",     "wuling",     "Chiny", false),
            ("MG",         "mg",         "Chiny", false),

            // Producenci elektrykow
            ("Rivian",     "rivian",     "USA",       false),
            ("Lucid",      "lucid",      "USA",       true),
            ("Polestar",   "polestar",   "Szwecja",   true),
            ("VinFast",    "vinfast",    "Wietnam",   false),

            // Indie - kategoria dotad calkowicie nieobsluzona
            ("Tata",       "tata",       "Indie", false),
            ("Mahindra",   "mahindra",   "Indie", false),

            // Pozostale braki masowe wskazane w audycie
            ("Cupra",      "cupra",      "Hiszpania", false),
            ("Alpine",     "alpine",     "Francja",   true),
            ("Daewoo",     "daewoo",     "Korea Południowa", false),
            ("Acura",      "acura",      "Japonia",   true),
            ("GMC",        "gmc",        "USA",       false),
            ("Lincoln",    "lincoln",    "USA",       true),
        };

        // Match on the same normalized key the UNIQUE index enforces, so a brand that already
        // exists under a different casing/spacing is found rather than duplicated.
        var existing = db.Brands.Include(b => b.Categories).AsEnumerable()
            .GroupBy(b => NKey(b.Name))
            .ToDictionary(g => g.Key, g => g.OrderBy(b => b.Id).First());

        var added = 0;
        var linked = 0;
        foreach (var (name, slug, country, luxury) in wanted)
        {
            if (existing.TryGetValue(NKey(name), out var brand))
            {
                // Already present - only ever ADD the missing category link and fill EMPTY
                // metadata. Never overwrite a value someone curated.
                brand.Categories ??= new List<VehicleCategory>();
                if (brand.Categories.All(c => c.Id != carCat.Id)) { brand.Categories.Add(carCat); linked++; }
                if (string.IsNullOrWhiteSpace(brand.OriginCountry)) brand.OriginCountry = country;
                continue;
            }

            db.Brands.Add(new Brand
            {
                Name = name,
                Slug = slug,
                OriginCountry = country,
                IsLuxury = luxury,
                Categories = new List<VehicleCategory> { carCat },
            });
            added++;
        }

        if (added > 0 || linked > 0)
        {
            db.SaveChanges();
            logger.LogInformation("[TaxonomyGap] Brands: added {Added}, linked to auta-osobowe {Linked}", added, linked);
        }
    }

    // ── Podtypy: Akcesoria i Uslugi motoryzacyjne ────────────────────────────────────────────
    // Both categories exist but have ZERO subtypes, so the add-listing form offers no way to say
    // what kind of accessory or service the listing actually is.
    private static void SeedAccessoryAndServiceSubtypes(AppDbContext db, ILogger logger)
    {
        var groups = new (string CategorySlug, string[] Subtypes)[]
        {
            ("akcesoria", new[]
            {
                "Bagażniki i boxy dachowe", "Fotele i pokrowce", "Dywaniki", "Multimedia i nawigacja",
                "Kamery i rejestratory", "Akcesoria do wnętrza", "Akcesoria zewnętrzne",
                "Detailing i kosmetyka", "Akcesoria tuningowe", "Haki i przyczepki",
                "Foteliki dziecięce", "Narzędzia i warsztat",
            }),
            ("uslugi-motoryzacyjne", new[]
            {
                "Mechanik", "Serwis samochodowy", "Detailing", "Myjnia", "Lakiernik", "Blacharz",
                "Tuning", "Chip tuning", "Diagnostyka", "Geometria kół", "Wulkanizacja",
                "Pomoc drogowa", "Transport samochodów", "Wypożyczalnia", "Rzeczoznawca",
                "Zabezpieczenia samochodów", "Folie ochronne PPF", "Powłoki ceramiczne",
                "Wrapping", "Renowacja klasyków",
            }),
        };

        var added = 0;
        foreach (var (slug, names) in groups)
        {
            var cat = db.VehicleCategories.FirstOrDefault(c => c.Slug == slug);
            if (cat is null) continue;

            var have = db.VehicleSubtypes.Where(v => v.VehicleCategoryId == cat.Id)
                .AsEnumerable().Select(v => NKey(v.Name)).ToHashSet();

            var order = db.VehicleSubtypes.Where(v => v.VehicleCategoryId == cat.Id)
                .Select(v => (int?)v.SortOrder).Max() ?? 0;

            foreach (var name in names)
            {
                if (!have.Add(NKey(name))) continue;
                db.VehicleSubtypes.Add(new VehicleSubtype
                {
                    VehicleCategoryId = cat.Id,
                    Name = name,
                    Slug = Slugify($"{slug}-{name}"),
                    SortOrder = ++order,
                });
                added++;
            }
        }

        if (added > 0)
        {
            db.SaveChanges();
            logger.LogInformation("[TaxonomyGap] Added {Count} subtype(s) for akcesoria/uslugi", added);
        }
    }

    private sealed record AttrSpec(string Key, string Label, AttributeDataType Type, string? Unit = null, string[]? Options = null);

    // ── Atrybuty dla kategorii, ktore nie mialy zadnych ──────────────────────────────────────
    // quady-atv, skutery-wodne and autobusy had zero AttributeDefinitions, so their listings had
    // no category-specific fields and no filters at all. Kampery/naczepy/wozki-widlowe had one.
    private static void SeedMissingCategoryAttributes(AppDbContext db, ILogger logger)
    {
        var byCategory = new (string Slug, AttrSpec[] Specs)[]
        {
            ("quady-atv", new[]
            {
                new AttrSpec("pojemnoscSilnika", "Pojemność silnika", AttributeDataType.Number, "cm3"),
                new AttrSpec("mocKM", "Moc", AttributeDataType.Number, "KM"),
                new AttrSpec("napedQuad", "Napęd", AttributeDataType.Select, null, new[] { "2x4", "4x4", "4x4 z blokadą", "Elektryczny" }),
                new AttrSpec("rodzajQuad", "Rodzaj", AttributeDataType.Select, null, new[] { "Sportowy", "Użytkowy", "ATV", "UTV / SSV", "Dziecięcy" }),
                new AttrSpec("skrzyniaQuad", "Skrzynia biegów", AttributeDataType.Select, null, new[] { "Manualna", "Automatyczna", "CVT", "Półautomatyczna" }),
                new AttrSpec("homologacja", "Homologacja drogowa", AttributeDataType.Boolean),
                new AttrSpec("wyciagarka", "Wyciągarka", AttributeDataType.Boolean),
            }),
            ("skutery-wodne", new[]
            {
                new AttrSpec("pojemnoscSilnika", "Pojemność silnika", AttributeDataType.Number, "cm3"),
                new AttrSpec("mocKM", "Moc", AttributeDataType.Number, "KM"),
                new AttrSpec("liczbaOsob", "Liczba osób", AttributeDataType.Number),
                new AttrSpec("motogodziny", "Motogodziny", AttributeDataType.Number, "mth"),
                new AttrSpec("typSkuteraWodnego", "Typ", AttributeDataType.Select, null, new[] { "Rekreacyjny", "Sportowy", "Turystyczny", "Stojący" }),
                new AttrSpec("przyczepaWZestawie", "Przyczepa w zestawie", AttributeDataType.Boolean),
            }),
            ("autobusy", new[]
            {
                new AttrSpec("liczbaMiejsc", "Liczba miejsc", AttributeDataType.Number),
                new AttrSpec("typAutobusu", "Typ", AttributeDataType.Select, null, new[] { "Miejski", "Turystyczny", "Szkolny", "Minibus", "Międzymiastowy" }),
                new AttrSpec("mocKM", "Moc", AttributeDataType.Number, "KM"),
                new AttrSpec("normaEmisji", "Norma emisji", AttributeDataType.Select, null, new[] { "Euro 3", "Euro 4", "Euro 5", "Euro 6" }),
                new AttrSpec("klimatyzacjaAutobus", "Klimatyzacja", AttributeDataType.Boolean),
                new AttrSpec("windaDlaNiepelnosprawnych", "Winda dla niepełnosprawnych", AttributeDataType.Boolean),
                new AttrSpec("wc", "WC", AttributeDataType.Boolean),
            }),
            ("kampery", new[]
            {
                new AttrSpec("typZabudowy", "Typ zabudowy", AttributeDataType.Select, null, new[] { "Kampervan", "Półintegra", "Integra", "Alkowa", "Przyczepa kempingowa" }),
                new AttrSpec("liczbaMiejscDoSpania", "Miejsca do spania", AttributeDataType.Number),
                new AttrSpec("dmc", "DMC", AttributeDataType.Number, "kg"),
                new AttrSpec("dlugoscCalkowita", "Długość całkowita", AttributeDataType.Decimal, "m"),
                new AttrSpec("lazienka", "Łazienka", AttributeDataType.Boolean),
                new AttrSpec("ogrzewanie", "Ogrzewanie postojowe", AttributeDataType.Boolean),
                new AttrSpec("panelSloneczny", "Panel słoneczny", AttributeDataType.Boolean),
                new AttrSpec("markiza", "Markiza", AttributeDataType.Boolean),
            }),
            ("wozki-widlowe", new[]
            {
                new AttrSpec("udzwig", "Udźwig", AttributeDataType.Number, "kg"),
                new AttrSpec("wysokoscPodnoszenia", "Wysokość podnoszenia", AttributeDataType.Number, "mm"),
                new AttrSpec("rodzajNapeduWozka", "Napęd", AttributeDataType.Select, null, new[] { "Elektryczny", "Diesel", "LPG", "Spalinowy" }),
                new AttrSpec("motogodziny", "Motogodziny", AttributeDataType.Number, "mth"),
                new AttrSpec("masztTyp", "Typ masztu", AttributeDataType.Select, null, new[] { "Duplex", "Triplex", "Standardowy" }),
            }),
        };

        // Only category-wide definitions here (no subtype/brand scoping), so the natural key is
        // (category, key) - matching the UNIQUE index added in Etap 1 with the NULL scope columns
        // collapsed to 0.
        var added = 0;
        foreach (var (slug, specs) in byCategory)
        {
            var cat = db.VehicleCategories.FirstOrDefault(c => c.Slug == slug);
            if (cat is null) continue;

            var have = db.AttributeDefinitions
                .Where(d => d.VehicleCategoryId == cat.Id && d.VehicleSubtypeId == null
                         && d.BrandId == null && d.ModelId == null && d.GenerationId == null && d.TrimId == null)
                .AsEnumerable().Select(d => NKey(d.Key)).ToHashSet();

            var order = db.AttributeDefinitions.Where(d => d.VehicleCategoryId == cat.Id)
                .Select(d => (int?)d.SortOrder).Max() ?? 0;

            foreach (var spec in specs)
            {
                if (!have.Add(NKey(spec.Key))) continue;
                db.AttributeDefinitions.Add(new AttributeDefinition
                {
                    VehicleCategoryId = cat.Id,
                    Key = spec.Key,
                    LabelPl = spec.Label,
                    DataType = spec.Type,
                    Unit = spec.Unit,
                    OptionsJson = spec.Options != null ? JsonSerializer.Serialize(spec.Options) : null,
                    IsRequired = false,
                    // Same rule the existing seeder uses: booleans and selects make good facets.
                    IsFilterable = spec.Type is AttributeDataType.Boolean or AttributeDataType.Select or AttributeDataType.Number,
                    IsSearchable = false,
                    IsActive = true,
                    SortOrder = ++order,
                });
                added++;
            }
        }

        if (added > 0)
        {
            db.SaveChanges();
            logger.LogInformation("[TaxonomyGap] Added {Count} attribute definition(s) for previously empty categories", added);
        }
    }

    // ── Grupy czesci ────────────────────────────────────────────────────────────────────────
    // The audit listed "turbiny" and "performance" as missing top-level part groups. Added as NEW
    // groups only - the existing "Elektryka i elektronika" is deliberately left merged rather than
    // split, because splitting it would have to re-point existing part listings, and that is an
    // irreversible data change waiting on a product decision.
    private static void SeedMissingPartCategories(AppDbContext db, ILogger logger)
    {
        var groups = new (string Name, string[] Subs)[]
        {
            ("Turbosprężarki i doładowanie", new[]
            {
                "Turbosprężarki", "Kompresory", "Intercoolery", "Zawory upustowe",
                "Aktuatory i sterowanie", "Przewody doładowania",
            }),
            ("Performance i sport", new[]
            {
                "Układ dolotowy", "Wydech sportowy", "Zawieszenie gwintowane", "Hamulce wyczynowe",
                "Sprzęgła wzmocnione", "Chłodnice oleju", "Elektronika wyczynowa", "Klatki i pasy",
            }),
        };

        var have = db.PartCategories.AsEnumerable().Select(p => NKey(p.Name)).ToHashSet();
        var order = db.PartCategories.Select(p => (int?)p.SortOrder).Max() ?? 0;

        var added = 0;
        foreach (var (name, subs) in groups)
        {
            if (!have.Add(NKey(name))) continue;
            db.PartCategories.Add(new PartCategory
            {
                Name = name,
                SortOrder = ++order,
                Subcategories = subs.Select((sn, i) => new PartSubcategory { Name = sn, SortOrder = i + 1 }).ToList(),
            });
            added++;
        }

        if (added > 0)
        {
            db.SaveChanges();
            logger.LogInformation("[TaxonomyGap] Added {Count} part category/-ies", added);
        }
    }

    private static string Slugify(string s)
    {
        var lowered = NKey(s)
            .Replace("ą", "a").Replace("ć", "c").Replace("ę", "e").Replace("ł", "l")
            .Replace("ń", "n").Replace("ó", "o").Replace("ś", "s").Replace("ź", "z").Replace("ż", "z");
        var chars = lowered.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }
}
