using cars_website_api.CarsWebsite.Domain.Entities;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace cars_website_api.CarsWebsite.Data;

// Seeds the global reference-data core (Faza 0). Idempotent by natural code (ISO / IANA name), so
// it is safe on every startup and only inserts what is missing. This is the STARTER set that makes
// the geo cascade, currencies and languages work out of the box; the full GeoNames city import
// (~150k rows) is a separate background job that appends to `cities` later.
public static class GeoSeeder
{
    public static void Seed(AppDbContext db, ILogger logger)
    {
        try
        {
            SeedContinents(db);
            SeedCurrencies(db);
            SeedLanguages(db);
            SeedTimeZones(db);
            db.SaveChanges();

            SeedCountries(db);
            db.SaveChanges();

            SeedPolishRegions(db);
            db.SaveChanges(); // persist regions so SeedCities can link cities to them

            SeedCities(db);
            SeedStarterExchangeRates(db);
            db.SaveChanges();

            logger.LogWarning("[GEO] GeoSeeder done: countries={C} currencies={Cur} languages={L} cities={City}",
                db.Countries.Count(), db.Currencies.Count(), db.Languages.Count(), db.Cities.Count());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[GEO] GeoSeeder failed: {Msg}", ex.Message);
        }
    }

    private static void SeedContinents(AppDbContext db)
    {
        var have = db.Continents.Select(c => c.Code).ToHashSet();
        var rows = new (string Code, string Name)[] {
            ("EU","Europe"), ("AS","Asia"), ("NA","North America"), ("SA","South America"),
            ("AF","Africa"), ("OC","Oceania"), ("AN","Antarctica"),
        };
        foreach (var (code, name) in rows)
            if (!have.Contains(code)) db.Continents.Add(new Continent { Code = code, Name = name });
    }

    private static void SeedCurrencies(AppDbContext db)
    {
        var have = db.Currencies.Select(c => c.Iso).ToHashSet();
        // Iso, Symbol, Name, Decimals, Position
        var rows = new (string Iso, string Sym, string Name, byte Dec, string Pos)[] {
            ("EUR","€","Euro",2,"pre"), ("PLN","zł","Polish złoty",2,"post"), ("USD","$","US dollar",2,"pre"),
            ("GBP","£","Pound sterling",2,"pre"), ("CHF","CHF","Swiss franc",2,"pre"), ("CZK","Kč","Czech koruna",2,"post"),
            ("SEK","kr","Swedish krona",2,"post"), ("NOK","kr","Norwegian krone",2,"post"), ("DKK","kr","Danish krone",2,"post"),
            ("HUF","Ft","Hungarian forint",2,"post"), ("RON","lei","Romanian leu",2,"post"), ("BGN","лв","Bulgarian lev",2,"post"),
            ("UAH","₴","Ukrainian hryvnia",2,"post"), ("TRY","₺","Turkish lira",2,"pre"), ("JPY","¥","Japanese yen",0,"pre"),
            ("CNY","¥","Chinese yuan",2,"pre"), ("CAD","$","Canadian dollar",2,"pre"), ("AUD","$","Australian dollar",2,"pre"),
            ("AED","د.إ","UAE dirham",2,"pre"), ("BRL","R$","Brazilian real",2,"pre"), ("INR","₹","Indian rupee",2,"pre"),
            ("ZAR","R","South African rand",2,"pre"), ("MXN","$","Mexican peso",2,"pre"),
        };
        foreach (var r in rows)
            if (!have.Contains(r.Iso))
                db.Currencies.Add(new Currency { Iso = r.Iso, Symbol = r.Sym, Name = r.Name, Decimals = r.Dec, SymbolPosition = r.Pos });
    }

    private static void SeedLanguages(AppDbContext db)
    {
        var have = db.Languages.Select(l => l.Iso1).ToHashSet();
        // Iso1, Endonym, English, IsRtl
        var rows = new (string Iso, string End, string Eng, bool Rtl)[] {
            ("pl","Polski","Polish",false), ("en","English","English",false), ("de","Deutsch","German",false),
            ("fr","Français","French",false), ("es","Español","Spanish",false), ("it","Italiano","Italian",false),
            ("nl","Nederlands","Dutch",false), ("cs","Čeština","Czech",false), ("uk","Українська","Ukrainian",false),
            ("ru","Русский","Russian",false), ("sv","Svenska","Swedish",false), ("da","Dansk","Danish",false),
            ("no","Norsk","Norwegian",false), ("fi","Suomi","Finnish",false), ("hu","Magyar","Hungarian",false),
            ("ro","Română","Romanian",false), ("pt","Português","Portuguese",false), ("tr","Türkçe","Turkish",false),
            ("ar","العربية","Arabic",true), ("he","עברית","Hebrew",true), ("zh","中文","Chinese",false),
            ("ja","日本語","Japanese",false),
        };
        foreach (var r in rows)
            if (!have.Contains(r.Iso))
                db.Languages.Add(new Language { Iso1 = r.Iso, Endonym = r.End, EnglishName = r.Eng, IsRtl = r.Rtl });
    }

    private static void SeedTimeZones(AppDbContext db)
    {
        var have = db.TimeZones.Select(t => t.IanaName).ToHashSet();
        var rows = new (string Iana, int Off, string Disp)[] {
            ("Europe/Warsaw",60,"CET"), ("Europe/Berlin",60,"CET"), ("Europe/Paris",60,"CET"),
            ("Europe/London",0,"GMT"), ("Europe/Madrid",60,"CET"), ("Europe/Rome",60,"CET"),
            ("Europe/Amsterdam",60,"CET"), ("Europe/Prague",60,"CET"), ("Europe/Kyiv",120,"EET"),
            ("Europe/Stockholm",60,"CET"), ("Europe/Bucharest",120,"EET"), ("Europe/Istanbul",180,"TRT"),
            ("America/New_York",-300,"EST"), ("America/Chicago",-360,"CST"), ("America/Los_Angeles",-480,"PST"),
            ("America/Toronto",-300,"EST"), ("America/Sao_Paulo",-180,"BRT"), ("Asia/Dubai",240,"GST"),
            ("Asia/Tokyo",540,"JST"), ("Asia/Shanghai",480,"CST"), ("Asia/Kolkata",330,"IST"),
            ("Australia/Sydney",600,"AEST"), ("Africa/Johannesburg",120,"SAST"), ("America/Mexico_City",-360,"CST"),
        };
        foreach (var r in rows)
            if (!have.Contains(r.Iana))
                db.TimeZones.Add(new AppTimeZone { IanaName = r.Iana, UtcOffsetMinutes = r.Off, DisplayName = r.Disp });
    }

    private static void SeedCountries(AppDbContext db)
    {
        var have = db.Countries.Select(c => c.Iso2).ToHashSet();
        var cont = db.Continents.ToDictionary(c => c.Code, c => c.Id);
        var cur = db.Currencies.ToDictionary(c => c.Iso, c => c.Id);
        var lang = db.Languages.ToDictionary(l => l.Iso1, l => l.Id);
        var tz = db.TimeZones.ToDictionary(t => t.IanaName, t => t.Id);

        int? C(string k) => cont.TryGetValue(k, out var v) ? v : null;
        int? Cur(string k) => cur.TryGetValue(k, out var v) ? v : null;
        int? L(string k) => lang.TryGetValue(k, out var v) ? v : null;
        int? Z(string k) => tz.TryGetValue(k, out var v) ? v : null;

        // Iso2, Iso3, Name, Native, Continent, Currency, Lang, Tz, Phone, Measure, Drive
        var rows = new (string I2, string I3, string N, string Nat, string Co, string Cu, string La, string Tz, string Ph, string Ms, string Dr)[] {
            ("PL","POL","Poland","Polska","EU","PLN","pl","Europe/Warsaw","+48","metric","R"),
            ("DE","DEU","Germany","Deutschland","EU","EUR","de","Europe/Berlin","+49","metric","R"),
            ("FR","FRA","France","France","EU","EUR","fr","Europe/Paris","+33","metric","R"),
            ("GB","GBR","United Kingdom","United Kingdom","EU","GBP","en","Europe/London","+44","imperial","L"),
            ("US","USA","United States","United States","NA","USD","en","America/New_York","+1","imperial","R"),
            ("IT","ITA","Italy","Italia","EU","EUR","it","Europe/Rome","+39","metric","R"),
            ("ES","ESP","Spain","España","EU","EUR","es","Europe/Madrid","+34","metric","R"),
            ("NL","NLD","Netherlands","Nederland","EU","EUR","nl","Europe/Amsterdam","+31","metric","R"),
            ("BE","BEL","Belgium","België","EU","EUR","nl","Europe/Amsterdam","+32","metric","R"),
            ("CZ","CZE","Czechia","Česko","EU","CZK","cs","Europe/Prague","+420","metric","R"),
            ("SK","SVK","Slovakia","Slovensko","EU","EUR","cs","Europe/Prague","+421","metric","R"),
            ("AT","AUT","Austria","Österreich","EU","EUR","de","Europe/Berlin","+43","metric","R"),
            ("CH","CHE","Switzerland","Schweiz","EU","CHF","de","Europe/Berlin","+41","metric","R"),
            ("UA","UKR","Ukraine","Україна","EU","UAH","uk","Europe/Kyiv","+380","metric","R"),
            ("SE","SWE","Sweden","Sverige","EU","SEK","sv","Europe/Stockholm","+46","metric","R"),
            ("NO","NOR","Norway","Norge","EU","NOK","no","Europe/Stockholm","+47","metric","R"),
            ("DK","DNK","Denmark","Danmark","EU","DKK","da","Europe/Stockholm","+45","metric","R"),
            ("FI","FIN","Finland","Suomi","EU","EUR","fi","Europe/Bucharest","+358","metric","R"),
            ("HU","HUN","Hungary","Magyarország","EU","HUF","hu","Europe/Warsaw","+36","metric","R"),
            ("RO","ROU","Romania","România","EU","RON","ro","Europe/Bucharest","+40","metric","R"),
            ("BG","BGR","Bulgaria","България","EU","BGN","en","Europe/Bucharest","+359","metric","R"),
            ("PT","PRT","Portugal","Portugal","EU","EUR","pt","Europe/London","+351","metric","R"),
            ("IE","IRL","Ireland","Éire","EU","EUR","en","Europe/London","+353","metric","L"),
            ("TR","TUR","Turkey","Türkiye","AS","TRY","tr","Europe/Istanbul","+90","metric","R"),
            ("CA","CAN","Canada","Canada","NA","CAD","en","America/Toronto","+1","metric","R"),
            ("MX","MEX","Mexico","México","NA","MXN","es","America/Mexico_City","+52","metric","R"),
            ("BR","BRA","Brazil","Brasil","SA","BRL","pt","America/Sao_Paulo","+55","metric","R"),
            ("AE","ARE","United Arab Emirates","الإمارات","AS","AED","ar","Asia/Dubai","+971","metric","R"),
            ("JP","JPN","Japan","日本","AS","JPY","ja","Asia/Tokyo","+81","metric","L"),
            ("CN","CHN","China","中国","AS","CNY","zh","Asia/Shanghai","+86","metric","R"),
            ("IN","IND","India","भारत","AS","INR","en","Asia/Kolkata","+91","metric","L"),
            ("AU","AUS","Australia","Australia","OC","AUD","en","Australia/Sydney","+61","metric","L"),
            ("ZA","ZAF","South Africa","South Africa","AF","ZAR","en","Africa/Johannesburg","+27","metric","L"),
        };
        foreach (var r in rows)
        {
            if (have.Contains(r.I2)) continue;
            db.Countries.Add(new Country {
                Iso2 = r.I2, Iso3 = r.I3, Name = r.N, NativeName = r.Nat,
                ContinentId = C(r.Co), DefaultCurrencyId = Cur(r.Cu), DefaultLanguageId = L(r.La),
                DefaultTimeZoneId = Z(r.Tz), PhonePrefix = r.Ph, MeasurementSystem = r.Ms, DrivingSide = r.Dr,
                IsActive = true,
            });
        }
    }

    private static void SeedPolishRegions(AppDbContext db)
    {
        var pl = db.Countries.FirstOrDefault(c => c.Iso2 == "PL");
        if (pl == null) return;
        var have = db.Regions.Where(r => r.CountryId == pl.Id).Select(r => r.Name).ToHashSet();
        var voiv = new[] {
            "Dolnośląskie","Kujawsko-pomorskie","Lubelskie","Lubuskie","Łódzkie","Małopolskie","Mazowieckie",
            "Opolskie","Podkarpackie","Podlaskie","Pomorskie","Śląskie","Świętokrzyskie","Warmińsko-mazurskie",
            "Wielkopolskie","Zachodniopomorskie",
        };
        foreach (var v in voiv)
            if (!have.Contains(v)) db.Regions.Add(new Region { CountryId = pl.Id, Name = v, Type = "voivodeship" });
    }

    private static void SeedCities(AppDbContext db)
    {
        var byIso = db.Countries.ToDictionary(c => c.Iso2, c => c.Id);
        // Starter major cities so the country->city cascade is demonstrable immediately. Full
        // GeoNames import (region-linked, ~150k rows) runs as a later background job.
        // Iso2, City, Ascii, Lat, Lng, Population
        var rows = new (string Iso, string Name, string Ascii, double Lat, double Lng, int Pop)[] {
            ("PL","Warszawa","Warszawa",52.2297,21.0122,1790658), ("PL","Kraków","Krakow",50.0647,19.9450,779115),
            ("PL","Łódź","Lodz",51.7592,19.4560,677286), ("PL","Wrocław","Wroclaw",51.1079,17.0385,641607),
            ("PL","Poznań","Poznan",52.4064,16.9252,534813), ("PL","Gdańsk","Gdansk",54.3520,18.6466,470907),
            ("PL","Szczecin","Szczecin",53.4285,14.5528,401907), ("PL","Katowice","Katowice",50.2649,19.0238,294510),
            ("DE","Berlin","Berlin",52.5200,13.4050,3769495), ("DE","Hamburg","Hamburg",53.5511,9.9937,1845229),
            ("DE","München","Munchen",48.1351,11.5820,1471508), ("DE","Köln","Koln",50.9375,6.9603,1085664),
            ("DE","Frankfurt","Frankfurt",50.1109,8.6821,753056),
            ("FR","Paris","Paris",48.8566,2.3522,2161000), ("FR","Marseille","Marseille",43.2965,5.3698,861635),
            ("FR","Lyon","Lyon",45.7640,4.8357,513275),
            ("GB","London","London",51.5074,-0.1278,8982000), ("GB","Manchester","Manchester",53.4808,-2.2426,553230),
            ("GB","Birmingham","Birmingham",52.4862,-1.8904,1141816),
            ("US","New York","New York",40.7128,-74.0060,8419000), ("US","Los Angeles","Los Angeles",34.0522,-118.2437,3980000),
            ("US","Chicago","Chicago",41.8781,-87.6298,2716000), ("US","Houston","Houston",29.7604,-95.3698,2328000),
            ("NL","Amsterdam","Amsterdam",52.3676,4.9041,821752), ("NL","Rotterdam","Rotterdam",51.9244,4.4777,651446),
            ("CZ","Praha","Praha",50.0755,14.4378,1309000), ("IT","Roma","Roma",41.9028,12.4964,2873000),
            ("IT","Milano","Milano",45.4642,9.1900,1352000), ("ES","Madrid","Madrid",40.4168,-3.7038,3223000),
            ("ES","Barcelona","Barcelona",41.3851,2.1734,1620000), ("UA","Київ","Kyiv",50.4501,30.5234,2884000),
        };
        var plRegions = db.Regions.Where(r => r.Country!.Iso2 == "PL").ToList();
        // best-effort region link for the PL starter cities (by well-known mapping)
        var plCityRegion = new Dictionary<string, string> {
            ["Warszawa"]="Mazowieckie", ["Kraków"]="Małopolskie", ["Łódź"]="Łódzkie", ["Wrocław"]="Dolnośląskie",
            ["Poznań"]="Wielkopolskie", ["Gdańsk"]="Pomorskie", ["Szczecin"]="Zachodniopomorskie", ["Katowice"]="Śląskie",
        };
        foreach (var r in rows)
        {
            if (!byIso.TryGetValue(r.Iso, out var countryId)) continue;
            if (db.Cities.Any(c => c.CountryId == countryId && c.Name == r.Name)) continue;
            int? regionId = null;
            if (r.Iso == "PL" && plCityRegion.TryGetValue(r.Name, out var rn))
                regionId = plRegions.FirstOrDefault(x => x.Name == rn)?.Id;
            db.Cities.Add(new City {
                CountryId = countryId, RegionId = regionId, Name = r.Name, AsciiName = r.Ascii,
                Latitude = r.Lat, Longitude = r.Lng, Population = r.Pop,
            });
        }
    }

    // Starter FX rates to EUR (approximate, fixed seed date so re-runs are idempotent via the
    // (CurrencyId, AsOf) unique index). A daily ECB/exchangerate.host job should append fresh rows;
    // this only bootstraps PriceEur so cross-currency sort/filter works from day one.
    private static void SeedStarterExchangeRates(AppDbContext db)
    {
        var seedDate = new DateTime(2024, 1, 1);
        var byIso = db.Currencies.ToDictionary(c => c.Iso, c => c.Id);
        var existing = db.ExchangeRates.Where(e => e.AsOf == seedDate).Select(e => e.CurrencyId).ToHashSet();
        var rates = new (string Iso, decimal ToEur)[] {
            ("EUR", 1.0m), ("PLN", 0.23m), ("USD", 0.92m), ("GBP", 1.17m), ("CHF", 1.04m), ("CZK", 0.040m),
            ("SEK", 0.088m), ("NOK", 0.086m), ("DKK", 0.134m), ("HUF", 0.0026m), ("RON", 0.20m), ("BGN", 0.51m),
            ("UAH", 0.024m), ("TRY", 0.030m), ("JPY", 0.0062m), ("CNY", 0.13m), ("CAD", 0.68m), ("AUD", 0.61m),
            ("AED", 0.25m), ("BRL", 0.18m), ("INR", 0.011m), ("ZAR", 0.050m), ("MXN", 0.054m),
        };
        foreach (var (iso, toEur) in rates)
            if (byIso.TryGetValue(iso, out var cid) && !existing.Contains(cid))
                db.ExchangeRates.Add(new ExchangeRate { CurrencyId = cid, RateToEur = toEur, AsOf = seedDate });
    }
}
