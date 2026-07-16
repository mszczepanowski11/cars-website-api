using System.Text;
using System.Xml.Linq;
using CarsWebsite;
using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.DTOs.Advert;
using cars_website_api.CarsWebsite.DTOs.Partner;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.EntityFrameworkCore;

// v1 scope: core fields only (title/description/price/brand/model/year/mileage/fuel/gearbox/
// power/vin/city/region/images), all imported adverts filed under "Auta osobowe". Brand/Model/
// FuelType/Gearbox are matched by exact case-insensitive name; if a partner references one that
// doesn't exist yet, it's created on the fly (see GetOrCreate* below) rather than rejecting the
// item - every auto-created row is recorded in the import log for admin review. Only items that
// are missing a hard-required field or fail CreateCarAdvertAsync's own validation are skipped.
public class PartnerImportService : IPartnerImportService
{
    private const int MaxErrorSummaryLength = 8000;

    private readonly AppDbContext _context;
    private readonly IAdvertService _advertService;

    public PartnerImportService(AppDbContext context, IAdvertService advertService)
    {
        _context = context;
        _advertService = advertService;
    }

    public async Task<PartnerImportLogResponseDto> ImportAsync(Partner partner, string content, PartnerFeedFormat format)
    {
        var log = new PartnerImportLog
        {
            PartnerId = partner.Id,
            Format = format,
            StartedAt = DateTime.UtcNow,
        };
        _context.PartnerImportLogs.Add(log);

        var errors = new List<string>();
        List<PartnerFeedItem> items;
        try
        {
            items = format == PartnerFeedFormat.Xml ? ParseXml(content) : ParseCsv(content);
        }
        catch (Exception ex)
        {
            errors.Add($"Nie udało się sparsować pliku: {ex.Message}");
            items = new List<PartnerFeedItem>();
        }

        log.ItemsTotal = items.Count;

        var carCategory = await _context.VehicleCategories.FirstOrDefaultAsync(c => c.Slug == "auta-osobowe");
        if (carCategory == null)
        {
            log.CompletedAt = DateTime.UtcNow;
            log.ErrorSummary = "Brak kategorii 'Auta osobowe' w bazie - import nie może być wykonany.";
            await _context.SaveChangesAsync();
            return new PartnerImportLogResponseDto
            {
                Id = log.Id, PartnerId = log.PartnerId, Format = log.Format.ToString(), StartedAt = log.StartedAt,
                CompletedAt = log.CompletedAt, ItemsTotal = log.ItemsTotal, ItemsFailed = log.ItemsTotal, ErrorSummary = log.ErrorSummary,
            };
        }

        // Grouped rather than ToDictionaryAsync directly: taxonomy tables have no unique
        // constraint on Name, and a duplicate (e.g. inconsistent casing entered elsewhere)
        // must not crash the whole import run - first match wins.
        var brandsByName = (await _context.Brands.ToListAsync())
            .GroupBy(b => b.Name.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var fuelTypesByName = (await _context.FuelTypes.ToListAsync())
            .GroupBy(f => f.Name.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var gearboxesByName = (await _context.Gearboxes.ToListAsync())
            .GroupBy(g => g.Name.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var modelsByBrandId = new Dictionary<int, Dictionary<string, Model>>();

        var rowNumber = 0;
        foreach (var item in items)
        {
            rowNumber++;
            var createdNotes = new List<string>();
            try
            {
                await ImportItemAsync(item, partner, carCategory, brandsByName, fuelTypesByName, gearboxesByName, modelsByBrandId, log, createdNotes);
                foreach (var note in createdNotes) errors.Add($"wiersz {rowNumber} ({item.ExternalId}): {note}");
            }
            catch (Exception ex)
            {
                foreach (var note in createdNotes) errors.Add($"wiersz {rowNumber} ({item.ExternalId}): {note}");
                log.ItemsFailed++;
                errors.Add($"wiersz {rowNumber} ({item.ExternalId}): {ex.Message}");
            }
        }

        partner.LastImportAt = DateTime.UtcNow;
        log.CompletedAt = DateTime.UtcNow;
        var summary = string.Join("\n", errors);
        log.ErrorSummary = summary.Length > MaxErrorSummaryLength ? summary[..MaxErrorSummaryLength] + "\n... (obcięto)" : summary;
        if (log.ErrorSummary.Length == 0) log.ErrorSummary = null;

        await _context.SaveChangesAsync();

        return new PartnerImportLogResponseDto
        {
            Id = log.Id,
            PartnerId = log.PartnerId,
            Format = log.Format.ToString(),
            StartedAt = log.StartedAt,
            CompletedAt = log.CompletedAt,
            ItemsTotal = log.ItemsTotal,
            ItemsCreated = log.ItemsCreated,
            ItemsUpdated = log.ItemsUpdated,
            ItemsFailed = log.ItemsFailed,
            ErrorSummary = log.ErrorSummary,
        };
    }

    private async Task ImportItemAsync(
        PartnerFeedItem item,
        Partner partner,
        VehicleCategory carCategory,
        Dictionary<string, Brand> brandsByName,
        Dictionary<string, FuelType> fuelTypesByName,
        Dictionary<string, Gearbox> gearboxesByName,
        Dictionary<int, Dictionary<string, Model>> modelsByBrandId,
        PartnerImportLog log,
        List<string> createdNotes)
    {
        // Manual bounds checks, not DataAnnotations: this path builds CreateCarAdvertDto/
        // UpdateCarAdvertDto directly and calls IAdvertService, bypassing the ModelState
        // validation that only runs for requests bound through a controller action.
        if (string.IsNullOrWhiteSpace(item.ExternalId))
            throw new ArgumentException("brak ExternalId");
        if (string.IsNullOrWhiteSpace(item.Title))
            throw new ArgumentException("brak Title");
        if (item.Price <= 0 || item.Price > 10_000_000)
            throw new ArgumentException("nieprawidłowa cena");
        if (item.Year < 1900 || item.Year > 2030)
            throw new ArgumentException("nieprawidłowy rok");
        if (item.Mileage < 0 || item.Mileage > 2_000_000)
            throw new ArgumentException("nieprawidłowy przebieg");

        if (item.Title.Length > 200) item.Title = item.Title[..200];
        if (item.Description?.Length > 5000) item.Description = item.Description[..5000];

        if (string.IsNullOrWhiteSpace(item.BrandName))
            throw new ArgumentException("brak marki");
        var brand = await GetOrCreateBrandAsync(item.BrandName, carCategory, brandsByName, createdNotes);

        if (!modelsByBrandId.TryGetValue(brand.Id, out var brandModels))
        {
            brandModels = (await _context.Models.Where(m => m.BrandId == brand.Id).ToListAsync())
                .GroupBy(m => m.Name.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
            modelsByBrandId[brand.Id] = brandModels;
        }

        if (string.IsNullOrWhiteSpace(item.ModelName))
            throw new ArgumentException("brak modelu");
        var model = await GetOrCreateModelAsync(item.ModelName, brand, brandModels, createdNotes);

        FuelType? fuelType = null;
        if (!string.IsNullOrWhiteSpace(item.FuelTypeName))
            fuelType = await GetOrCreateFuelTypeAsync(item.FuelTypeName, fuelTypesByName, createdNotes);

        Gearbox? gearbox = null;
        if (!string.IsNullOrWhiteSpace(item.GearboxName))
            gearbox = await GetOrCreateGearboxAsync(item.GearboxName, gearboxesByName, createdNotes);

        var existing = await _context.CarAdverts
            .FirstOrDefaultAsync(a => a.PartnerId == partner.Id && a.ExternalId == item.ExternalId);

        int advertId;
        if (existing == null)
        {
            var createDto = new CreateCarAdvertDto
            {
                VehicleCategoryId = carCategory.Id,
                BrandId = brand.Id,
                ModelId = model.Id,
                FuelTypeId = fuelType?.Id,
                GearboxId = gearbox?.Id,
                Year = item.Year,
                Mileage = item.Mileage,
                Price = item.Price,
                Title = item.Title,
                Description = item.Description,
                City = item.City,
                Region = item.Region,
                Vin = item.Vin,
                PowerHP = item.PowerHP,
                // A Partner's LinkedUser is always a dealer-managed Business account (enforced
                // at Partner creation) selling stock it already owns, never a private first-owner listing.
                Condition = "used",
                SellerType = "dealer",
            };

            advertId = await _advertService.CreateCarAdvertAsync(createDto, partner.LinkedUserId);

            var advert = await _context.CarAdverts.FirstAsync(a => a.Id == advertId);
            advert.PartnerId = partner.Id;
            advert.ExternalId = item.ExternalId;

            log.ItemsCreated++;
        }
        else
        {
            var updateDto = new UpdateCarAdvertDto
            {
                VehicleCategoryId = carCategory.Id,
                BrandId = brand.Id,
                ModelId = model.Id,
                FuelTypeId = fuelType?.Id,
                GearboxId = gearbox?.Id,
                Year = item.Year,
                Mileage = item.Mileage,
                Price = item.Price,
                Title = item.Title,
                Description = item.Description,
                City = item.City,
                Region = item.Region,
                Vin = item.Vin,
                PowerHP = item.PowerHP,
                Condition = "used",
                SellerType = "dealer",
            };

            await _advertService.UpdateCarAdvertAsync(existing.Id, updateDto, partner.LinkedUserId);
            advertId = existing.Id;

            log.ItemsUpdated++;
        }

        if (item.ImageUrls.Count > 0)
        {
            var existingImages = await _context.AdvertImages.Where(i => i.AdvertId == advertId).ToListAsync();
            _context.AdvertImages.RemoveRange(existingImages);

            for (var i = 0; i < item.ImageUrls.Count; i++)
            {
                _context.AdvertImages.Add(new AdvertImage
                {
                    AdvertId = advertId,
                    Url = item.ImageUrls[i],
                    Order = i,
                    IsMain = i == 0,
                });
            }
        }

        await _context.SaveChangesAsync();
    }

    // Partners commonly list vehicles from brands/models we haven't onboarded yet - rather than
    // rejecting the whole item, the taxonomy is grown on the fly. This does mean a typo in a
    // partner's feed (e.g. "Fiatt") creates a bogus brand; createdNotes records every auto-created
    // row into the import log precisely so an admin can spot and merge/fix that after the fact.
    private async Task<Brand> GetOrCreateBrandAsync(string name, VehicleCategory carCategory, Dictionary<string, Brand> brandsByName, List<string> createdNotes)
    {
        var key = name.Trim().ToLowerInvariant();
        if (brandsByName.TryGetValue(key, out var existing)) return existing;

        var brand = new Brand { Name = ClampName(name), Slug = Slugify(name), Categories = new List<VehicleCategory> { carCategory } };
        _context.Brands.Add(brand);
        await _context.SaveChangesAsync();

        brandsByName[key] = brand;
        createdNotes.Add($"utworzono nową markę '{brand.Name}'");
        return brand;
    }

    private async Task<Model> GetOrCreateModelAsync(string name, Brand brand, Dictionary<string, Model> brandModels, List<string> createdNotes)
    {
        var key = name.Trim().ToLowerInvariant();
        if (brandModels.TryGetValue(key, out var existing)) return existing;

        var model = new Model { Name = ClampName(name), Slug = Slugify(name), BrandId = brand.Id };
        _context.Models.Add(model);
        await _context.SaveChangesAsync();

        brandModels[key] = model;
        createdNotes.Add($"utworzono nowy model '{model.Name}' dla marki '{brand.Name}'");
        return model;
    }

    private async Task<FuelType> GetOrCreateFuelTypeAsync(string name, Dictionary<string, FuelType> fuelTypesByName, List<string> createdNotes)
    {
        var key = name.Trim().ToLowerInvariant();
        if (fuelTypesByName.TryGetValue(key, out var existing)) return existing;

        var fuelType = new FuelType { Name = ClampName(name) };
        _context.FuelTypes.Add(fuelType);
        await _context.SaveChangesAsync();

        fuelTypesByName[key] = fuelType;
        createdNotes.Add($"utworzono nowy rodzaj paliwa '{fuelType.Name}'");
        return fuelType;
    }

    private async Task<Gearbox> GetOrCreateGearboxAsync(string name, Dictionary<string, Gearbox> gearboxesByName, List<string> createdNotes)
    {
        var key = name.Trim().ToLowerInvariant();
        if (gearboxesByName.TryGetValue(key, out var existing)) return existing;

        var gearbox = new Gearbox { Name = ClampName(name) };
        _context.Gearboxes.Add(gearbox);
        await _context.SaveChangesAsync();

        gearboxesByName[key] = gearbox;
        createdNotes.Add($"utworzono nową skrzynię biegów '{gearbox.Name}'");
        return gearbox;
    }

    private static readonly Dictionary<char, string> DiacriticMap = new()
    {
        ['ą'] = "a", ['ć'] = "c", ['ę'] = "e", ['ł'] = "l", ['ń'] = "n",
        ['ó'] = "o", ['ś'] = "s", ['ź'] = "z", ['ż'] = "z",
        ['Ą'] = "a", ['Ć'] = "c", ['Ę'] = "e", ['Ł'] = "l", ['Ń'] = "n",
        ['Ó'] = "o", ['Ś'] = "s", ['Ź'] = "z", ['Ż'] = "z",
        ['ä'] = "a", ['ö'] = "o", ['ü'] = "u", ['ß'] = "ss",
        ['č'] = "c", ['š'] = "s", ['ě'] = "e", ['é'] = "e", ['è'] = "e",
    };

    private static string Slugify(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (DiacriticMap.TryGetValue(ch, out var repl)) sb.Append(repl);
            else if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else sb.Append('-');
        }
        var slug = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "-{2,}", "-").Trim('-');
        if (slug.Length == 0) slug = "brand";
        return slug.Length > 100 ? slug[..100].TrimEnd('-') : slug;
    }

    private static string ClampName(string name)
    {
        var trimmed = name.Trim();
        return trimmed.Length > 100 ? trimmed[..100] : trimmed;
    }

    private static List<PartnerFeedItem> ParseXml(string content)
    {
        var doc = XDocument.Parse(content);
        var items = new List<PartnerFeedItem>();

        foreach (var node in doc.Descendants("Advert"))
        {
            items.Add(new PartnerFeedItem
            {
                ExternalId = (string?)node.Element("ExternalId") ?? string.Empty,
                Title = (string?)node.Element("Title") ?? string.Empty,
                Description = (string?)node.Element("Description"),
                Price = (decimal?)node.Element("Price") ?? 0,
                BrandName = (string?)node.Element("Brand") ?? string.Empty,
                ModelName = (string?)node.Element("Model") ?? string.Empty,
                Year = (int?)node.Element("Year") ?? 0,
                Mileage = (int?)node.Element("Mileage") ?? 0,
                FuelTypeName = (string?)node.Element("FuelType"),
                GearboxName = (string?)node.Element("Gearbox"),
                PowerHP = (int?)node.Element("PowerHP"),
                Vin = (string?)node.Element("Vin"),
                City = (string?)node.Element("City"),
                Region = (string?)node.Element("Region"),
                ImageUrls = node.Element("Images")?.Elements("Image")
                    .Select(e => e.Value.Trim())
                    .Where(v => v.Length > 0)
                    .ToList() ?? new List<string>(),
            });
        }

        return items;
    }

    private static List<PartnerFeedItem> ParseCsv(string content)
    {
        var lines = content.Replace("\r\n", "\n").Replace("\r", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return new List<PartnerFeedItem>();

        var header = SplitCsvLine(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
        var items = new List<PartnerFeedItem>();

        for (var i = 1; i < lines.Length; i++)
        {
            var fields = SplitCsvLine(lines[i]);
            string Get(string col)
            {
                var idx = header.IndexOf(col);
                return idx >= 0 && idx < fields.Count ? fields[idx].Trim() : string.Empty;
            }

            decimal.TryParse(Get("price"), out var price);
            int.TryParse(Get("year"), out var year);
            int.TryParse(Get("mileage"), out var mileage);
            int.TryParse(Get("powerhp"), out var powerHp);

            var imageUrls = Get("imageurls").Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(u => u.Trim())
                .Where(u => u.Length > 0)
                .ToList();

            items.Add(new PartnerFeedItem
            {
                ExternalId = Get("externalid"),
                Title = Get("title"),
                Description = string.IsNullOrEmpty(Get("description")) ? null : Get("description"),
                Price = price,
                BrandName = Get("brand"),
                ModelName = Get("model"),
                Year = year,
                Mileage = mileage,
                FuelTypeName = string.IsNullOrEmpty(Get("fueltype")) ? null : Get("fueltype"),
                GearboxName = string.IsNullOrEmpty(Get("gearbox")) ? null : Get("gearbox"),
                PowerHP = powerHp > 0 ? powerHp : null,
                Vin = string.IsNullOrEmpty(Get("vin")) ? null : Get("vin"),
                City = string.IsNullOrEmpty(Get("city")) ? null : Get("city"),
                Region = string.IsNullOrEmpty(Get("region")) ? null : Get("region"),
                ImageUrls = imageUrls,
            });
        }

        return items;
    }

    // Minimal RFC 4180 CSV parser: handles double-quoted fields (with embedded commas and
    // doubled "" as an escaped quote), since a well-formed partner feed can't reliably avoid
    // commas inside a description field.
    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { result.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
        }
        result.Add(sb.ToString());
        return result;
    }

    private class PartnerFeedItem
    {
        public string ExternalId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public string BrandName { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public int Year { get; set; }
        public int Mileage { get; set; }
        public string? FuelTypeName { get; set; }
        public string? GearboxName { get; set; }
        public int? PowerHP { get; set; }
        public string? Vin { get; set; }
        public string? City { get; set; }
        public string? Region { get; set; }
        public List<string> ImageUrls { get; set; } = new();
    }
}
