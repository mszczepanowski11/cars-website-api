using cars_website_api.CarsWebsite.Domain.Entities;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace cars_website_api.CarsWebsite.Data;

// Faza 1 of the category/attribute restructure: backfills Brand.OriginCountry/IsLuxury for every
// brand this seeder can confidently classify. Backs the "Samochody amerykańskie/japońskie/
// chińskie" and "Samochody luksusowe" filters on Auta osobowe as brand-level metadata (set once
// per brand) instead of a duplicated per-advert flag.
//
// Idempotent: only fills brands whose OriginCountry is currently null, so re-running on every
// deploy never overwrites a value an admin has since edited by hand via admin/vehicle-data.vue.
// Brands not in the map below are left null on purpose - a guess is worse than an honest gap,
// and the admin UI lets someone fill them in deliberately.
public static class BrandMetadataSeeder
{
    private static readonly Dictionary<string, (string Origin, bool Luxury)> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // Niemcy
        ["BMW"] = ("Niemcy", false), ["BMW Motorrad"] = ("Niemcy", false),
        ["Mercedes-Benz"] = ("Niemcy", false), ["Audi"] = ("Niemcy", false),
        ["Volkswagen"] = ("Niemcy", false), ["Opel"] = ("Niemcy", false),
        ["Porsche"] = ("Niemcy", true), ["Smart"] = ("Niemcy", false),
        ["MAN"] = ("Niemcy", false), ["Setra"] = ("Niemcy", false),
        ["Krone"] = ("Niemcy", false), ["Schmitz Cargobull"] = ("Niemcy", false),
        ["Kögel"] = ("Niemcy", false), ["Kogel"] = ("Niemcy", false),
        ["Fliegl"] = ("Niemcy", false), ["Meiller"] = ("Niemcy", false),
        ["Humbaur"] = ("Niemcy", false), ["Böckmann"] = ("Niemcy", false),
        ["Liebherr"] = ("Niemcy", false), ["Wacker Neuson"] = ("Niemcy", false),
        ["Jungheinrich"] = ("Niemcy", false), ["Linde"] = ("Niemcy", false),
        ["Still"] = ("Niemcy", false), ["Hymer"] = ("Niemcy", false),
        ["Knaus"] = ("Niemcy", false), ["Dethleffs"] = ("Niemcy", false),
        ["Weinsberg"] = ("Niemcy", false), ["Bürstner"] = ("Niemcy", false),
        ["Burstner"] = ("Niemcy", false), ["Sunlight"] = ("Niemcy", false),
        ["Malibu"] = ("Niemcy", false), ["Husqvarna"] = ("Szwecja", false),
        ["MZ"] = ("Niemcy", false), ["Zündapp"] = ("Niemcy", false), ["Zundapp"] = ("Niemcy", false),
        ["Deutz-Fahr"] = ("Niemcy", false), ["Claas"] = ("Niemcy", false),
        ["Wartburg"] = ("Niemcy", false), ["Trabant"] = ("Niemcy", false),
        ["Bavaria Yachts"] = ("Niemcy", false), ["Schwarzmüller"] = ("Austria", false),
        ["Schwarzmuller"] = ("Austria", false), ["KTM"] = ("Austria", false),
        ["Steyr"] = ("Austria", false),

        // Japonia
        ["Toyota"] = ("Japonia", false), ["Lexus"] = ("Japonia", true),
        ["Honda"] = ("Japonia", false), ["Nissan"] = ("Japonia", false),
        ["Infiniti"] = ("Japonia", true), ["Mazda"] = ("Japonia", false),
        ["Mitsubishi"] = ("Japonia", false), ["Suzuki"] = ("Japonia", false),
        ["Subaru"] = ("Japonia", false), ["Daihatsu"] = ("Japonia", false),
        ["Yamaha"] = ("Japonia", false), ["Kawasaki"] = ("Japonia", false),
        ["Isuzu"] = ("Japonia", false), ["Hitachi"] = ("Japonia", false),
        ["Hitachi Construction"] = ("Japonia", false), ["Komatsu"] = ("Japonia", false),
        ["Kubota"] = ("Japonia", false), ["Takeuchi"] = ("Japonia", false),
        ["Sea-Doo"] = ("Kanada", false),

        // Chiny
        ["BYD"] = ("Chiny", false), ["Great Wall"] = ("Chiny", false),
        ["MG"] = ("Chiny", false), ["CFMoto"] = ("Chiny", false), ["CFMOTO"] = ("Chiny", false),
        ["Kymco"] = ("Tajwan", false), ["Linhai"] = ("Chiny", false),
        ["TGB"] = ("Tajwan", false), ["Maxus"] = ("Chiny", false),
        ["Baoli"] = ("Chiny", false),

        // USA
        ["Ford"] = ("USA", false), ["Ford Trucks"] = ("Turcja", false),
        ["Chevrolet"] = ("USA", false), ["Cadillac"] = ("USA", true),
        ["Chrysler"] = ("USA", false), ["Dodge"] = ("USA", false),
        ["Jeep"] = ("USA", false), ["Tesla"] = ("USA", false),
        ["Harley-Davidson"] = ("USA", false), ["Indian"] = ("USA", false),
        ["Polaris"] = ("USA", false), ["Can-Am"] = ("Kanada", false),
        ["Caterpillar"] = ("USA", false), ["John Deere"] = ("USA", false),
        ["Case IH"] = ("USA", false), ["New Holland"] = ("USA", false),
        ["Bobcat"] = ("USA", false), ["Terex"] = ("USA", false),
        ["Crown"] = ("USA", false), ["Hyster"] = ("USA", false),
        ["Sea Ray"] = ("USA", false), ["Bayliner"] = ("USA", false),
        ["Four Winns"] = ("USA", false), ["Arctic Cat"] = ("USA", false),

        // Wielka Brytania
        ["Jaguar"] = ("Wielka Brytania", true), ["Land Rover"] = ("Wielka Brytania", false),
        ["Range Rover"] = ("Wielka Brytania", true), ["Bentley"] = ("Wielka Brytania", true),
        ["Rolls-Royce"] = ("Wielka Brytania", true), ["Aston Martin"] = ("Wielka Brytania", true),
        ["McLaren"] = ("Wielka Brytania", true), ["Mini"] = ("Wielka Brytania", false),
        ["Triumph"] = ("Wielka Brytania", false), ["Royal Enfield"] = ("Indie", false),
        ["Rover"] = ("Wielka Brytania", false), ["JCB"] = ("Wielka Brytania", false),
        ["Ifor Williams"] = ("Wielka Brytania", false),

        // Włochy
        ["Ferrari"] = ("Włochy", true), ["Lamborghini"] = ("Włochy", true),
        ["Maserati"] = ("Włochy", true), ["Alfa Romeo"] = ("Włochy", false),
        ["Fiat"] = ("Włochy", false), ["Lancia"] = ("Włochy", false),
        ["Abarth"] = ("Włochy", false), ["Ducati"] = ("Włochy", false),
        ["Aprilia"] = ("Włochy", false), ["Piaggio"] = ("Włochy", false),
        ["Vespa"] = ("Włochy", false), ["MV Agusta"] = ("Włochy", true),
        ["Moto Guzzi"] = ("Włochy", false), ["Iveco"] = ("Włochy", false),
        ["Iveco Bus"] = ("Włochy", false), ["Benelli"] = ("Włochy", false),
        ["Merlo"] = ("Włochy", false), ["Same"] = ("Włochy", false),
        ["Selva"] = ("Włochy", false), ["Azimut"] = ("Włochy", true),
        ["Feretti"] = ("Włochy", true), ["Galeon"] = ("Polska", false),

        // Francja
        ["Renault"] = ("Francja", false), ["Renault Trucks"] = ("Francja", false),
        ["Peugeot"] = ("Francja", false), ["Citroën"] = ("Francja", false),
        ["Citroen"] = ("Francja", false), ["DS"] = ("Francja", true),
        ["Bugatti"] = ("Francja", true), ["Jeanneau"] = ("Francja", false),
        ["Beneteau"] = ("Francja", false), ["Rapido"] = ("Francja", false),
        ["Chausson"] = ("Francja", false), ["Pilote"] = ("Francja", false),
        ["Trigano"] = ("Francja", false),

        // Szwecja
        ["Volvo"] = ("Szwecja", false), ["Volvo Trucks"] = ("Szwecja", false),
        ["Volvo CE"] = ("Szwecja", false), ["Saab"] = ("Szwecja", false),
        ["Scania"] = ("Szwecja", false),

        // Korea
        ["Hyundai"] = ("Korea", false), ["Kia"] = ("Korea", false),
        ["Genesis"] = ("Korea", true), ["SsangYong"] = ("Korea", false),
        ["Doosan"] = ("Korea", false),

        // Czechy / Polska / inne europejskie
        ["Skoda"] = ("Czechy", false), ["Zetor"] = ("Czechy", false),
        ["Tatra"] = ("Czechy", false), ["Jawa"] = ("Czechy", false),
        ["Dacia"] = ("Rumunia", false), ["Lada"] = ("Rosja", false),
        ["KamAZ"] = ("Rosja", false), ["MAZ"] = ("Białoruś", false),
        ["Belarus/MTZ"] = ("Białoruś", false),
        ["FSO"] = ("Polska", false), ["Syrena"] = ("Polska", false),
        ["Star"] = ("Polska", false), ["Jelcz"] = ("Polska", false),
        ["Autosan"] = ("Polska", false), ["Nysa"] = ("Polska", false),
        ["Żuk"] = ("Polska", false), ["Zuk"] = ("Polska", false),
        ["Solaris"] = ("Polska", false), ["Solaris Bus & Coach"] = ("Polska", false),
        ["Wielton"] = ("Polska", false), ["Niewiadów"] = ("Polska", false),
        ["Niewiadow"] = ("Polska", false), ["Wiola"] = ("Polska", false),
        ["Krukowiak"] = ("Polska", false), ["Pronar"] = ("Polska", false),
        ["Ursus"] = ("Polska", false), ["WSK"] = ("Polska", false),
        ["Ikarus"] = ("Węgry", false), ["Neoplan"] = ("Niemcy", false),
        ["Irisbus"] = ("Francja", false),
    };

    public static void Seed(AppDbContext db, ILogger logger)
    {
        var brands = db.Brands.Where(b => b.OriginCountry == null).ToList();
        if (brands.Count == 0) return;

        int updated = 0;
        var unmatched = new List<string>();

        foreach (var brand in brands)
        {
            if (Map.TryGetValue(brand.Name, out var meta))
            {
                brand.OriginCountry = meta.Origin;
                brand.IsLuxury = meta.Luxury;
                updated++;
            }
            else
            {
                unmatched.Add(brand.Name);
            }
        }

        db.SaveChanges();
        logger.LogWarning(
            "[BRAND-METADATA] BrandMetadataSeeder done: updated={Updated} unmatched={UnmatchedCount} ({Unmatched})",
            updated, unmatched.Count, string.Join(", ", unmatched.Distinct().OrderBy(n => n)));
    }
}
