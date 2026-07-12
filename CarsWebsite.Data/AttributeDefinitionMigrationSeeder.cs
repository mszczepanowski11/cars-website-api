using cars_website_api.CarsWebsite.Domain.Entities;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace cars_website_api.CarsWebsite.Data;

// Faza 4 of the category/attribute restructure (crispy-riding-mochi.md): one-time backfill of
// AttributeDefinition rows for every extraFields/SUBTYPE_EXTRA_FIELDS entry in add-advert.vue that
// does NOT already map to a real CarAdvert column in submit() (i.e. everything that currently only
// ever reaches buildDescription()'s free-text dump, or - for most SUBTYPE_EXTRA_FIELDS entries -
// is silently dropped on submit entirely, since buildDescription() never reads subtype extras).
// Fields that DO map to a real column (condition, driveType, doors, seatsCount, color, colorFinish,
// euroNorm, co2, fuelConsumption*, torque, gvw/payload/axles/length variants, hasTachograph,
// hasRetarder, bodyVariant/trailerType/machineType/truckType, operatingWeight, workingWidth,
// maxDiggingDepth, bucketCapacity, tankCapacity) are intentionally excluded here - they stay real
// CarAdvert columns forever, per the plan's hard boundary. Idempotent by (VehicleCategoryId,
// VehicleSubtypeId, Key), same convention as ExternalTaxonomySeeder/BrandMetadataSeeder - safe to
// run on every startup.
public static class AttributeDefinitionMigrationSeeder
{
    private record DefSpec(string CategorySlug, string? SubtypeSlug, string Key, string LabelPl, AttributeDataType DataType, string? Unit, string[]? Options, bool IsRequired = false);

    private static readonly DefSpec[] Specs =
    [
        // ── auta-osobowe ────────────────────────────────────────────────────
        new("auta-osobowe", null, "firstOwner", "Pierwszy właściciel", AttributeDataType.Boolean, null, null),
        new("auta-osobowe", null, "noDamage", "Bezwypadkowy (potwierdzony)", AttributeDataType.Boolean, null, null),
        new("auta-osobowe", null, "hasASO", "Serwisowany w ASO", AttributeDataType.Boolean, null, null),
        new("auta-osobowe", null, "testDrive", "Możliwość jazdy próbnej", AttributeDataType.Boolean, null, null),
        new("auta-osobowe", null, "vatMargin", "Faktura VAT-marża", AttributeDataType.Boolean, null, null),
        new("auta-osobowe", null, "registeredInPoland", "Zarejestrowany w Polsce", AttributeDataType.Boolean, null, null),
        new("auta-osobowe", null, "rightHandDrive", "Kierownica po prawej stronie (RHD)", AttributeDataType.Boolean, null, null),
        new("auta-osobowe", null, "tuning", "Tuning / modyfikacje", AttributeDataType.Boolean, null, null),

        // ── dostawcze ────────────────────────────────────────────────────────
        new("dostawcze", null, "loadingHeight", "Wysokość przestrzeni ładunkowej", AttributeDataType.Decimal, "m", null),
        new("dostawcze", null, "loadingWidth", "Szerokość przestrzeni ładunkowej", AttributeDataType.Decimal, "m", null),
        new("dostawcze", null, "hasAC", "Klimatyzacja", AttributeDataType.Boolean, null, null),
        new("dostawcze", null, "hasReverseCam", "Kamera cofania", AttributeDataType.Boolean, null, null),
        new("dostawcze", null, "hasLiftgate", "Winda załadowcza", AttributeDataType.Boolean, null, null),
        new("dostawcze", null, "firstOwner", "Pierwszy właściciel", AttributeDataType.Boolean, null, null),

        // ── ciezarowe ────────────────────────────────────────────────────────
        new("ciezarowe", null, "cabType", "Typ kabiny", AttributeDataType.Select, null,
            ["Normalna", "Niska", "Wysoka (sleeper)", "Mega / Super Space", "Crew cab (załogowa)"]),
        new("ciezarowe", null, "hasAC", "Klimatyzacja kabiny", AttributeDataType.Boolean, null, null),
        new("ciezarowe", null, "hasAPU", "Agregat postojowy (APU)", AttributeDataType.Boolean, null, null),
        new("ciezarowe", null, "hasHydraulics", "Hydraulika (PTO)", AttributeDataType.Boolean, null, null),
        new("ciezarowe", null, "hasLiftAxle", "Oś podnoszona (Lift Axle)", AttributeDataType.Boolean, null, null),
        new("ciezarowe", null, "hasADR", "Dopuszczenie ADR", AttributeDataType.Boolean, null, null),
        new("ciezarowe", null, "firstOwner", "Pierwszy właściciel", AttributeDataType.Boolean, null, null),

        // ── czesci ───────────────────────────────────────────────────────────
        new("czesci", null, "shipping", "Możliwa wysyłka", AttributeDataType.Boolean, null, null),
        new("czesci", null, "warranty", "Gwarancja na część", AttributeDataType.Boolean, null, null),

        // ── motocykle ────────────────────────────────────────────────────────
        new("motocykle", null, "motorcycleType", "Typ motocykla", AttributeDataType.Select, null,
            ["Sportowy / Supersport", "Turystyczny (Tourer)", "Adventure / Enduro drogowe", "Enduro / Cross / Off-road",
             "Cruiser / Chopper", "Naked / Streetfighter", "Café Racer / Scrambler", "Skuter",
             "Skuter 125 cm³ (AM)", "Trial", "Quad / ATV", "Z wózkiem bocznym", "Elektryczny", "Inny"], IsRequired: true),
        new("motocykle", null, "hasABS", "ABS", AttributeDataType.Boolean, null, null),
        new("motocykle", null, "hasTCS", "Kontrola trakcji (TCS)", AttributeDataType.Boolean, null, null),
        new("motocykle", null, "hasQuickshifter", "Quickshifter (bi-directional)", AttributeDataType.Boolean, null, null),
        new("motocykle", null, "hasHeatedGrips", "Podgrzewane manetki", AttributeDataType.Boolean, null, null),
        new("motocykle", null, "hasCruiseControl", "Tempomat", AttributeDataType.Boolean, null, null),
        new("motocykle", null, "hasRideByWire", "Ride-by-Wire / tryby jazdy", AttributeDataType.Boolean, null, null),
        new("motocykle", null, "hasLedLights", "Oświetlenie LED", AttributeDataType.Boolean, null, null),
        new("motocykle", null, "hasSaddlebags", "Kufry / sakwy", AttributeDataType.Boolean, null, null),
        new("motocykle", null, "firstOwner", "Pierwszy właściciel", AttributeDataType.Boolean, null, null),
        new("motocykle", null, "noDamage", "Bezwypadkowy", AttributeDataType.Boolean, null, null),
        new("motocykle", null, "testDrive", "Możliwość jazdy próbnej", AttributeDataType.Boolean, null, null),

        // ── przyczepy ────────────────────────────────────────────────────────
        new("przyczepy", null, "width", "Szerokość całkowita", AttributeDataType.Decimal, "m", null),
        new("przyczepy", null, "height", "Wysokość całkowita", AttributeDataType.Decimal, "m", null),
        new("przyczepy", null, "hasHydraulics", "Hydraulika", AttributeDataType.Boolean, null, null),
        new("przyczepy", null, "hasLift", "Winda załadunkowa", AttributeDataType.Boolean, null, null),
        new("przyczepy", null, "hasBrakes", "Hamulec najazdowy", AttributeDataType.Boolean, null, null),

        // ── rolnicze ─────────────────────────────────────────────────────────
        new("rolnicze", null, "engineHp", "Moc silnika (HP)", AttributeDataType.Number, "HP", null),
        new("rolnicze", null, "frontLoader", "Ładowacz czołowy (TUR)", AttributeDataType.Boolean, null, null),
        new("rolnicze", null, "dualWheels", "Bliźniaki (koła bliźniacze)", AttributeDataType.Boolean, null, null),
        new("rolnicze", null, "frontPTO", "Przedni WOM", AttributeDataType.Boolean, null, null),
        new("rolnicze", null, "rearPTO", "Tylny WOM", AttributeDataType.Boolean, null, null),
        new("rolnicze", null, "gps", "GPS / Auto-steer", AttributeDataType.Boolean, null, null),
        new("rolnicze", null, "fourWD", "Napęd 4WD", AttributeDataType.Boolean, null, null),
        new("rolnicze", null, "cabinAC", "Klimatyzacja kabiny", AttributeDataType.Boolean, null, null),
        new("rolnicze", null, "isobus", "ISOBUS (ISO 11783)", AttributeDataType.Boolean, null, null),

        // ── budowlane ────────────────────────────────────────────────────────
        new("budowlane", null, "liftCapacity", "Udźwig / nośność", AttributeDataType.Number, "kg", null),
        new("budowlane", null, "workingHeight", "Wysokość robocza / zasięg", AttributeDataType.Decimal, "m", null),
        new("budowlane", null, "hasHydraulics", "Rozdzielacz hydrauliczny", AttributeDataType.Boolean, null, null),
        new("budowlane", null, "hasCabin", "Zamknięta kabina", AttributeDataType.Boolean, null, null),
        new("budowlane", null, "hasAC", "Klimatyzacja kabiny", AttributeDataType.Boolean, null, null),
        new("budowlane", null, "hasGPS", "GPS / system sterowania", AttributeDataType.Boolean, null, null),

        // ── maszyny ──────────────────────────────────────────────────────────
        new("maszyny", null, "liftCapacity", "Udźwig / nośność", AttributeDataType.Number, "kg", null),
        new("maszyny", null, "workingHeight", "Wysokość robocza / zasięg", AttributeDataType.Decimal, "m", null),
        new("maszyny", null, "hasAC", "Klimatyzacja kabiny", AttributeDataType.Boolean, null, null),
        new("maszyny", null, "hasCabin", "Zamknięta kabina", AttributeDataType.Boolean, null, null),

        // ── lodzie-i-jachty ──────────────────────────────────────────────────
        new("lodzie-i-jachty", null, "hullMaterial", "Materiał kadłuba", AttributeDataType.Select, null,
            ["Laminat / włókno szklane", "Aluminium", "Stal", "Drewno", "Ponton gumowy / PVC"]),
        new("lodzie-i-jachty", null, "lengthM", "Długość całkowita", AttributeDataType.Decimal, "m", null),

        // ── kampery ──────────────────────────────────────────────────────────
        new("kampery", null, "berths", "Liczba miejsc do spania", AttributeDataType.Number, null, null),

        // ── wozki-widlowe ────────────────────────────────────────────────────
        new("wozki-widlowe", null, "liftCapacity", "Udźwig", AttributeDataType.Number, "kg", null),

        // ── Subtypes: ciezarowe ──────────────────────────────────────────────
        new("ciezarowe", "ciagnik-siodlowy", "cabType", "Typ kabiny", AttributeDataType.Select, null,
            ["Kabina dzienna", "Kabina sypialnia", "Kabina Maxi"]),
        new("ciezarowe", "ciagnik-siodlowy", "suspension", "Zawieszenie tylne", AttributeDataType.Select, null,
            ["Resorowe", "Powietrzne"]),
        new("ciezarowe", "ciagnik-siodlowy", "axleConfig", "Konfiguracja osi", AttributeDataType.Select, null,
            ["4x2", "6x2", "6x4", "8x4"]),

        new("ciezarowe", "wywrotka", "bodySubtype", "Kierunek wysypu", AttributeDataType.Select, null,
            ["Tylny", "3-stronny", "Boczny"]),
        new("ciezarowe", "wywrotka", "dumpBodyMaterial", "Materiał skrzyni", AttributeDataType.Select, null,
            ["Stal", "Aluminium", "Polietylen"]),
        new("ciezarowe", "wywrotka", "volume", "Pojemność skrzyni (m³)", AttributeDataType.Decimal, "m³", null),

        new("ciezarowe", "chlodnia-ciezarowa", "tempMin", "Min. temperatura (°C)", AttributeDataType.Decimal, "°C", null),
        new("ciezarowe", "chlodnia-ciezarowa", "tempMax", "Max. temperatura (°C)", AttributeDataType.Decimal, "°C", null),
        new("ciezarowe", "chlodnia-ciezarowa", "atpCert", "Certyfikat ATP", AttributeDataType.Boolean, null, null),

        new("ciezarowe", "firanka", "loadingHeight", "Wysokość załadunku (m)", AttributeDataType.Decimal, "m", null),
        new("ciezarowe", "firanka", "volume", "Objętość ładowni (m³)", AttributeDataType.Decimal, "m³", null),
        new("ciezarowe", "firanka", "hasLiftgate", "Winda załadowcza", AttributeDataType.Boolean, null, null),

        new("ciezarowe", "cysterna", "tankMaterial", "Materiał zbiornika", AttributeDataType.Select, null,
            ["Stal nierdzewna", "Aluminium", "Tworzywo sztuczne"]),
        new("ciezarowe", "cysterna", "adrClass", "Klasa ADR", AttributeDataType.Select, null,
            ["Brak", "Klasa 1 (mat. wybuchowe)", "Klasa 2 (gazy)", "Klasa 3 (ciecze łatwopal.)", "Klasa 8 (substancje żrące)"]),

        // ── Subtypes: rolnicze ───────────────────────────────────────────────
        new("rolnicze", "ciagnik", "ptoRpm", "WOM (rpm)", AttributeDataType.Select, null,
            ["540 rpm", "1000 rpm", "540/1000 rpm", "ECO"]),
        new("rolnicze", "ciagnik", "hasFrontLinkage", "TUZ przedni", AttributeDataType.Boolean, null, null),
        new("rolnicze", "ciagnik", "hydraulicOutputs", "Wyjścia hydrauliczne", AttributeDataType.Number, "szt.", null),
        new("rolnicze", "ciagnik", "cabin", "Kabina", AttributeDataType.Boolean, null, null),

        new("rolnicze", "kombajn", "bodySubtype", "Typ uprawy", AttributeDataType.Select, null,
            ["Zbożowy", "Kukurydza", "Rzepak", "Uniwersalny"]),
        new("rolnicze", "kombajn", "hasStrawChopper", "Rozdrabniacz słomy", AttributeDataType.Boolean, null, null),

        new("rolnicze", "opryskiwacz", "selfPropelled", "Typ", AttributeDataType.Select, null,
            ["Samojezdny", "Zawieszany", "Przyczepiany"]),
        new("rolnicze", "opryskiwacz", "hasGps", "Prowadzenie GPS", AttributeDataType.Boolean, null, null),

        new("rolnicze", "prasa", "bodySubtype", "Typ bel", AttributeDataType.Select, null,
            ["Bele okrągłe", "Bele prostokątne"]),
        new("rolnicze", "prasa", "hasNetWrap", "Owijarka siatką", AttributeDataType.Boolean, null, null),

        new("rolnicze", "siewnik", "rowSpacing", "Rozstaw rzędów (cm)", AttributeDataType.Number, "cm", null),

        // ── Subtypes: budowlane ──────────────────────────────────────────────
        new("budowlane", "koparka", "undercarriage", "Podwozie", AttributeDataType.Select, null,
            ["Gąsienice gumowe", "Gąsienice stalowe", "Kołowe"]),
        new("budowlane", "koparka", "tailSwing", "Tylni zwis", AttributeDataType.Select, null,
            ["Standardowy", "Ograniczony", "Zerowy"]),

        new("budowlane", "minikopiarka", "hasOffsetBoom", "Offset ramię", AttributeDataType.Boolean, null, null),

        new("budowlane", "ladowarka", "liftHeight", "Wysokość podnoszenia (m)", AttributeDataType.Decimal, "m", null),
        new("budowlane", "ladowarka", "hasPalletForks", "Widelce paletowe", AttributeDataType.Boolean, null, null),
        new("budowlane", "ladowarka", "hasTelescopicArm", "Ramię teleskopowe", AttributeDataType.Boolean, null, null),

        new("budowlane", "spycharka", "bodySubtype", "Typ lemiesza", AttributeDataType.Select, null,
            ["Lemiesz prosty (S)", "Lemiesz U", "Lemiesz S-blade"]),
        new("budowlane", "spycharka", "hasRipper", "Spulchniacz", AttributeDataType.Boolean, null, null),

        new("budowlane", "walec", "bodySubtype", "Typ walca", AttributeDataType.Select, null,
            ["Jednostkowy (wibracyjny)", "Dwuwalcowy", "Ogumiony"]),
        new("budowlane", "walec", "hasVibration", "Wibrator", AttributeDataType.Boolean, null, null),

        new("budowlane", "zuraw", "bodySubtype", "Typ żurawia", AttributeDataType.Select, null,
            ["Wieżowy", "Mobilny", "Samojezdny"]),
        new("budowlane", "zuraw", "maxLoad", "Udźwig max (t)", AttributeDataType.Decimal, "t", null),
        new("budowlane", "zuraw", "maxBoom", "Długość wysięgnika (m)", AttributeDataType.Decimal, "m", null),
    ];

    public static void Seed(AppDbContext db, ILogger logger)
    {
        var categories = db.VehicleCategories.AsNoTracking().ToList()
            .GroupBy(c => c.Slug, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var subtypes = db.VehicleSubtypes.AsNoTracking().Where(s => s.Slug != null).ToList()
            .GroupBy(s => s.Slug!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Tracked (not AsNoTracking): existing rows whose metadata drifted from Specs (e.g. a label
        // typo fixed here, or - as happened once - a required flag corrected after the fact) get
        // their fields synced in place rather than silently staying stale forever.
        var existingDefs = db.AttributeDefinitions.ToList()
            .ToDictionary(ad => (ad.VehicleCategoryId, ad.VehicleSubtypeId, ad.Key));

        var sortCounters = new Dictionary<(int CategoryId, int? SubtypeId), int>();
        int added = 0, updated = 0, skippedNoCategory = 0, skippedNoSubtype = 0;

        foreach (var spec in Specs)
        {
            if (!categories.TryGetValue(spec.CategorySlug, out var category)) { skippedNoCategory++; continue; }

            int? subtypeId = null;
            if (spec.SubtypeSlug != null)
            {
                if (!subtypes.TryGetValue(spec.SubtypeSlug, out var subtype)) { skippedNoSubtype++; continue; }
                subtypeId = subtype.Id;
            }

            var optionsJson = spec.Options != null ? JsonSerializer.Serialize(spec.Options) : null;
            var naturalKey = (category.Id, subtypeId, spec.Key);

            if (existingDefs.TryGetValue(naturalKey, out var def))
            {
                if (def.LabelPl != spec.LabelPl || def.DataType != spec.DataType || def.Unit != spec.Unit ||
                    def.OptionsJson != optionsJson || def.IsRequired != spec.IsRequired)
                {
                    def.LabelPl = spec.LabelPl;
                    def.DataType = spec.DataType;
                    def.Unit = spec.Unit;
                    def.OptionsJson = optionsJson;
                    def.IsRequired = spec.IsRequired;
                    updated++;
                }
                continue;
            }

            var scopeKey = (category.Id, subtypeId);
            var sortOrder = sortCounters.TryGetValue(scopeKey, out var n) ? n : 0;
            sortCounters[scopeKey] = sortOrder + 1;

            var newDef = new AttributeDefinition
            {
                VehicleCategoryId = category.Id,
                VehicleSubtypeId = subtypeId,
                Key = spec.Key,
                LabelPl = spec.LabelPl,
                DataType = spec.DataType,
                Unit = spec.Unit,
                OptionsJson = optionsJson,
                IsRequired = spec.IsRequired,
                IsFilterable = spec.DataType is AttributeDataType.Boolean or AttributeDataType.Select,
                IsSearchable = false,
                IsActive = true,
                SortOrder = sortOrder,
            };
            db.AttributeDefinitions.Add(newDef);
            existingDefs[naturalKey] = newDef;
            added++;
        }

        if (added > 0 || updated > 0) db.SaveChanges();
        logger.LogWarning(
            "[ATTR-MIGRATION] AttributeDefinitionMigrationSeeder done: added={Added} updated={Updated} skippedNoCategory={SkippedNoCategory} skippedNoSubtype={SkippedNoSubtype}",
            added, updated, skippedNoCategory, skippedNoSubtype);
    }
}
