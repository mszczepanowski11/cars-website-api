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
// FuelType/Gearbox are resolved by exact case-insensitive name match only - no fuzzy matching.
// Unmatched or invalid items are logged as per-item errors and skipped rather than guessed at.
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

        var carCategoryId = await _context.VehicleCategories
            .Where(c => c.Slug == "auta-osobowe")
            .Select(c => c.Id)
            .FirstOrDefaultAsync();

        // Grouped rather than ToDictionaryAsync directly: taxonomy tables have no unique
        // constraint on Name, and a duplicate (e.g. inconsistent casing entered elsewhere)
        // must not crash the whole import run - first match wins.
        var brandsByName = (await _context.Brands.AsNoTracking().ToListAsync())
            .GroupBy(b => b.Name.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var fuelTypesByName = (await _context.FuelTypes.AsNoTracking().ToListAsync())
            .GroupBy(f => f.Name.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var gearboxesByName = (await _context.Gearboxes.AsNoTracking().ToListAsync())
            .GroupBy(g => g.Name.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var modelsByBrandId = new Dictionary<int, Dictionary<string, Model>>();

        var rowNumber = 0;
        foreach (var item in items)
        {
            rowNumber++;
            try
            {
                await ImportItemAsync(item, partner, carCategoryId, brandsByName, fuelTypesByName, gearboxesByName, modelsByBrandId, log);
            }
            catch (Exception ex)
            {
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
        int carCategoryId,
        Dictionary<string, Brand> brandsByName,
        Dictionary<string, FuelType> fuelTypesByName,
        Dictionary<string, Gearbox> gearboxesByName,
        Dictionary<int, Dictionary<string, Model>> modelsByBrandId,
        PartnerImportLog log)
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

        if (string.IsNullOrWhiteSpace(item.BrandName) || !brandsByName.TryGetValue(item.BrandName.Trim().ToLowerInvariant(), out var brand))
            throw new ArgumentException($"nieznana marka '{item.BrandName}'");

        if (!modelsByBrandId.TryGetValue(brand.Id, out var brandModels))
        {
            brandModels = (await _context.Models.AsNoTracking().Where(m => m.BrandId == brand.Id).ToListAsync())
                .GroupBy(m => m.Name.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
            modelsByBrandId[brand.Id] = brandModels;
        }

        if (string.IsNullOrWhiteSpace(item.ModelName) || !brandModels.TryGetValue(item.ModelName.Trim().ToLowerInvariant(), out var model))
            throw new ArgumentException($"nieznany model '{item.ModelName}' dla marki '{item.BrandName}'");

        FuelType? fuelType = null;
        if (!string.IsNullOrWhiteSpace(item.FuelTypeName) && !fuelTypesByName.TryGetValue(item.FuelTypeName.Trim().ToLowerInvariant(), out fuelType))
            throw new ArgumentException($"nieznany rodzaj paliwa '{item.FuelTypeName}'");

        Gearbox? gearbox = null;
        if (!string.IsNullOrWhiteSpace(item.GearboxName) && !gearboxesByName.TryGetValue(item.GearboxName.Trim().ToLowerInvariant(), out gearbox))
            throw new ArgumentException($"nieznana skrzynia biegów '{item.GearboxName}'");

        var existing = await _context.CarAdverts
            .FirstOrDefaultAsync(a => a.PartnerId == partner.Id && a.ExternalId == item.ExternalId);

        int advertId;
        if (existing == null)
        {
            var createDto = new CreateCarAdvertDto
            {
                VehicleCategoryId = carCategoryId,
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
                VehicleCategoryId = carCategoryId,
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
