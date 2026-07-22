using cars_website_api.CarsWebsite.Domain.Entities;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace cars_website_api.CarsWebsite.Data;

// "Inteligentny formularz": seeds brand-scoped equipment fields onto AttributeDefinition so that
// picking a brand in add-advert surfaces exactly that brand's characteristic options - BMW shows
// xDrive/Head-Up Display, VW shows DSG/DCC, Tesla shows Autopilot, etc. Every field is a boolean
// toggle scoped by BrandId within the "auta-osobowe" category (BrandId set, model/generation/trim
// null = "any model of that brand"). Admins can later narrow a field to a specific model/generation
// from the panel, or add whole new brands - this is only the starter set.
//
// Idempotent by (VehicleCategoryId, BrandId, Key): safe on every startup; existing rows are left
// untouched so admin edits (e.g. disabling one) survive re-seeding.
public static class VehicleEquipmentSeeder
{
    // brand name (as seeded in Brands) -> list of equipment labels
    private static readonly (string Brand, string[] Options)[] BrandEquipment =
    {
        ("BMW", new[] {
            "xDrive", "Adaptive Drive", "Integral Active Steering", "Head-Up Display", "Soft Close",
            "Night Vision", "Komfort Seats", "Harman Kardon", "Bowers & Wilkins", "Driving Assistant",
            "Active Cruise Control", "M Sport", "Luxury Line", "Shadow Line", "Panorama",
        }),
        ("Volkswagen", new[] {
            "Performance", "Clubsport", "DSG", "DCC", "VAQ", "Akrapovič", "Virtual Cockpit",
            "Discover Pro", "IQ.Light", "Launch Control",
        }),
        ("Mercedes-Benz", new[] {
            "AMG Line", "Burmester", "Distronic", "Multibeam LED", "Air Body Control",
            "Night Package", "4MATIC", "Digital Cockpit",
        }),
        ("Audi", new[] {
            "quattro", "Matrix LED", "Virtual Cockpit", "Bang & Olufsen", "S-Line",
            "RS Design", "Adaptive Air Suspension",
        }),
        ("Tesla", new[] {
            "Autopilot", "Enhanced Autopilot", "Full Self Driving", "Premium Interior",
            "Heat Pump", "Ryzen", "LFP/NCA",
        }),
    };

    public static void Seed(AppDbContext db, ILogger logger)
    {
        try
        {
            var carCategory = db.VehicleCategories.AsNoTracking()
                .FirstOrDefault(c => c.Slug == "auta-osobowe");
            if (carCategory == null) { logger.LogWarning("[EQUIPMENT] auta-osobowe category missing, skipping"); return; }

            var brandsByName = db.Brands.AsNoTracking().ToList()
                .GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // Existing brand-scoped rows in this category, keyed by (BrandId, Key), for idempotency.
            var existing = db.AttributeDefinitions
                .Where(ad => ad.VehicleCategoryId == carCategory.Id && ad.BrandId != null)
                .ToList()
                .ToDictionary(ad => (ad.BrandId!.Value, ad.Key), ad => ad);

            int added = 0, skippedNoBrand = 0;
            foreach (var (brandName, options) in BrandEquipment)
            {
                if (!brandsByName.TryGetValue(brandName, out var brand)) { skippedNoBrand++; continue; }

                int sort = 0;
                foreach (var label in options)
                {
                    var key = Slugify($"{brandName}-{label}"); // brand-prefixed so keys never collide
                    if (existing.ContainsKey((brand.Id, key))) { sort++; continue; }

                    db.AttributeDefinitions.Add(new AttributeDefinition
                    {
                        VehicleCategoryId = carCategory.Id,
                        BrandId = brand.Id,
                        Key = key,
                        LabelPl = label,
                        DataType = AttributeDataType.Boolean,
                        IsFilterable = true,
                        IsActive = true,
                        SortOrder = 100 + sort, // after the generic car fields
                    });
                    existing[(brand.Id, key)] = null!;
                    added++; sort++;
                }
            }

            if (added > 0) db.SaveChanges();
            logger.LogWarning("[EQUIPMENT] VehicleEquipmentSeeder done: added={Added} skippedNoBrand={Skipped}",
                added, skippedNoBrand);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EQUIPMENT] VehicleEquipmentSeeder failed: {Msg}", ex.Message);
        }
    }

    private static string Slugify(string s)
    {
        var lowered = s.Trim().ToLowerInvariant()
            .Replace("&", "and").Replace("/", "-").Replace(".", "").Replace("č", "c").Replace("ž", "z");
        var slug = System.Text.RegularExpressions.Regex.Replace(lowered, @"[^a-z0-9]+", "-").Trim('-');
        return slug.Length == 0 ? "opcja" : slug;
    }
}
