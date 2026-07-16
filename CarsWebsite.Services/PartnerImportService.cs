using System.Text;
using System.Xml.Linq;
using CarsWebsite;
using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.DTOs.Advert;
using cars_website_api.CarsWebsite.DTOs.Partner;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.EntityFrameworkCore;

// Multi-category import: a partner feed is not limited to cars - each item names its own
// Category (VehicleCategory slug, e.g. "auta-osobowe", "czesci", "budowlane", "opony"), so one
// partner account can mix car listings, parts, machinery, tires etc. in the same feed. Brand/
// Model/FuelType/Gearbox/PartCategory/PartSubcategory/VehicleSubtype are all optional per item -
// a parts or services listing simply won't set the ones that don't apply to it (the underlying
// CarAdvert entity and HierarchyValidationService already tolerate all of them being null, see
// CarAdvert.cs's own comment on nullable taxonomy FKs). Brand/Model/FuelType/Gearbox are matched
// by exact case-insensitive name and created on the fly if unknown (see GetOrCreate* below);
// PartCategory/PartSubcategory/VehicleSubtype are matched the same way but NOT auto-created,
// since those are small curated lists an admin defines rather than an open brand catalog - an
// unmatched one is just left unset and noted rather than failing the item. Every auto-created or
// unmatched-and-skipped reference is recorded in the import log for admin review.
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

        // Grouped rather than ToDictionaryAsync directly: taxonomy tables have no unique
        // constraint on Name/Slug, and a duplicate (e.g. inconsistent casing entered elsewhere)
        // must not crash the whole import run - first match wins.
        var categoriesBySlug = (await _context.VehicleCategories.ToListAsync())
            .GroupBy(c => c.Slug.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var brandsByName = (await _context.Brands.Include(b => b.Categories).ToListAsync())
            .GroupBy(b => b.Name.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var fuelTypesByName = (await _context.FuelTypes.ToListAsync())
            .GroupBy(f => f.Name.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var gearboxesByName = (await _context.Gearboxes.ToListAsync())
            .GroupBy(g => g.Name.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var partCategoriesByName = (await _context.PartCategories.ToListAsync())
            .GroupBy(p => p.Name.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var partSubcategoriesByName = (await _context.PartSubcategories.ToListAsync())
            .GroupBy(p => p.Name.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var modelsByBrandId = new Dictionary<int, Dictionary<string, Model>>();
        var subtypesByCategoryId = new Dictionary<int, Dictionary<string, VehicleSubtype>>();

        var rowNumber = 0;
        foreach (var item in items)
        {
            rowNumber++;
            var createdNotes = new List<string>();
            try
            {
                if (!categoriesBySlug.TryGetValue(item.CategorySlug.Trim().ToLowerInvariant(), out var category))
                    throw new ArgumentException($"nieznana kategoria '{item.CategorySlug}'");

                await ImportItemAsync(item, partner, category, brandsByName, fuelTypesByName, gearboxesByName,
                    partCategoriesByName, partSubcategoriesByName, modelsByBrandId, subtypesByCategoryId, log, createdNotes);
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
        VehicleCategory category,
        Dictionary<string, Brand> brandsByName,
        Dictionary<string, FuelType> fuelTypesByName,
        Dictionary<string, Gearbox> gearboxesByName,
        Dictionary<string, PartCategory> partCategoriesByName,
        Dictionary<string, PartSubcategory> partSubcategoriesByName,
        Dictionary<int, Dictionary<string, Model>> modelsByBrandId,
        Dictionary<int, Dictionary<string, VehicleSubtype>> subtypesByCategoryId,
        PartnerImportLog log,
        List<string> createdNotes)
    {
        // Manual bounds checks, not DataAnnotations: this path builds CreateCarAdvertDto/
        // UpdateCarAdvertDto directly and calls IAdvertService, bypassing the ModelState
        // validation that only runs for requests bound through a controller action. Only
        // ExternalId/Title/Category/Price are universal - everything else is category-dependent
        // and stays optional (see the class-level comment).
        if (string.IsNullOrWhiteSpace(item.ExternalId))
            throw new ArgumentException("brak ExternalId");
        if (string.IsNullOrWhiteSpace(item.Title))
            throw new ArgumentException("brak Title");
        if (item.Price <= 0 || item.Price > 10_000_000)
            throw new ArgumentException("nieprawidłowa cena");
        // Year 0 means "not supplied" - non-vehicle categories (czesci, uslugi-motoryzacyjne,
        // akcesoria...) have no year, so only validate the range when a value was actually given.
        if (item.Year != 0 && (item.Year < 1900 || item.Year > 2030))
            throw new ArgumentException("nieprawidłowy rok");
        if (item.Mileage < 0 || item.Mileage > 2_000_000)
            throw new ArgumentException("nieprawidłowy przebieg");

        if (item.Title.Length > 200) item.Title = item.Title[..200];
        if (item.Description?.Length > 5000) item.Description = item.Description[..5000];

        Brand? brand = null;
        Model? model = null;
        if (!string.IsNullOrWhiteSpace(item.BrandName))
        {
            brand = await GetOrCreateBrandAsync(item.BrandName, category, brandsByName, createdNotes);

            if (!string.IsNullOrWhiteSpace(item.ModelName))
            {
                if (!modelsByBrandId.TryGetValue(brand.Id, out var brandModels))
                {
                    brandModels = (await _context.Models.Where(m => m.BrandId == brand.Id).ToListAsync())
                        .GroupBy(m => m.Name.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
                    modelsByBrandId[brand.Id] = brandModels;
                }
                model = await GetOrCreateModelAsync(item.ModelName, brand, brandModels, createdNotes);
            }
        }

        FuelType? fuelType = null;
        if (!string.IsNullOrWhiteSpace(item.FuelTypeName))
            fuelType = await GetOrCreateFuelTypeAsync(item.FuelTypeName, fuelTypesByName, createdNotes);

        Gearbox? gearbox = null;
        if (!string.IsNullOrWhiteSpace(item.GearboxName))
            gearbox = await GetOrCreateGearboxAsync(item.GearboxName, gearboxesByName, createdNotes);

        PartCategory? partCategory = null;
        if (!string.IsNullOrWhiteSpace(item.PartCategoryName))
        {
            if (partCategoriesByName.TryGetValue(item.PartCategoryName.Trim().ToLowerInvariant(), out var pc)) partCategory = pc;
            else createdNotes.Add($"nieznana kategoria części '{item.PartCategoryName}' - pominięto");
        }

        PartSubcategory? partSubcategory = null;
        if (!string.IsNullOrWhiteSpace(item.PartSubcategoryName))
        {
            if (partSubcategoriesByName.TryGetValue(item.PartSubcategoryName.Trim().ToLowerInvariant(), out var psc)) partSubcategory = psc;
            else createdNotes.Add($"nieznana podkategoria części '{item.PartSubcategoryName}' - pominięto");
        }

        VehicleSubtype? subtype = null;
        if (!string.IsNullOrWhiteSpace(item.VehicleSubtypeName))
        {
            if (!subtypesByCategoryId.TryGetValue(category.Id, out var categorySubtypes))
            {
                categorySubtypes = (await _context.VehicleSubtypes.Where(s => s.VehicleCategoryId == category.Id).ToListAsync())
                    .GroupBy(s => s.Name.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
                subtypesByCategoryId[category.Id] = categorySubtypes;
            }
            if (categorySubtypes.TryGetValue(item.VehicleSubtypeName.Trim().ToLowerInvariant(), out var st)) subtype = st;
            else createdNotes.Add($"nieznany podtyp '{item.VehicleSubtypeName}' dla kategorii '{category.Name}' - pominięto");
        }

        var condition = item.ConditionText?.Trim().ToLowerInvariant() == "new" ? "new" : "used";

        var existing = await _context.CarAdverts
            .FirstOrDefaultAsync(a => a.PartnerId == partner.Id && a.ExternalId == item.ExternalId);

        int advertId;
        if (existing == null)
        {
            var createDto = new CreateCarAdvertDto
            {
                VehicleCategoryId = category.Id,
                VehicleSubtypeId = subtype?.Id,
                BrandId = brand?.Id,
                ModelId = model?.Id,
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
                PartCategoryId = partCategory?.Id,
                PartSubcategoryId = partSubcategory?.Id,
                CatalogNumber = item.CatalogNumber,
                OemNumber = item.OemNumber,
                PartManufacturer = item.PartManufacturer,
                Compatibility = item.Compatibility,
                AxleCount = item.AxleCount,
                Payload = item.Payload,
                CargoLength = item.CargoLength,
                CargoHeight = item.CargoHeight,
                Volume = item.Volume,
                OperatingWeightKg = item.OperatingWeightKg,
                WorkingWidthCm = item.WorkingWidthCm,
                MaxDiggingDepthM = item.MaxDiggingDepthM,
                BucketCapacityL = item.BucketCapacityL,
                TankCapacityL = item.TankCapacityL,
                // A Partner's LinkedUser is always a dealer-managed Business account (enforced
                // at Partner creation) selling stock it already owns, never a private first-owner listing.
                Condition = condition,
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
                VehicleCategoryId = category.Id,
                VehicleSubtypeId = subtype?.Id,
                BrandId = brand?.Id,
                ModelId = model?.Id,
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
                PartCategoryId = partCategory?.Id,
                PartSubcategoryId = partSubcategory?.Id,
                CatalogNumber = item.CatalogNumber,
                OemNumber = item.OemNumber,
                PartManufacturer = item.PartManufacturer,
                Compatibility = item.Compatibility,
                AxleCount = item.AxleCount,
                Payload = item.Payload,
                CargoLength = item.CargoLength,
                CargoHeight = item.CargoHeight,
                Volume = item.Volume,
                OperatingWeightKg = item.OperatingWeightKg,
                WorkingWidthCm = item.WorkingWidthCm,
                MaxDiggingDepthM = item.MaxDiggingDepthM,
                BucketCapacityL = item.BucketCapacityL,
                TankCapacityL = item.TankCapacityL,
                Condition = condition,
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

    // Partners commonly list brands/models we haven't onboarded yet - rather than rejecting the
    // whole item, the taxonomy is grown on the fly. This does mean a typo in a partner's feed
    // (e.g. "Fiatt") creates a bogus brand; createdNotes records every auto-created row into the
    // import log precisely so an admin can spot and merge/fix that after the fact.
    private async Task<Brand> GetOrCreateBrandAsync(string name, VehicleCategory category, Dictionary<string, Brand> brandsByName, List<string> createdNotes)
    {
        var key = name.Trim().ToLowerInvariant();
        if (brandsByName.TryGetValue(key, out var existing))
        {
            // The brand exists but may have been onboarded for a different category (e.g. a tire
            // brand later referenced by a car-parts feed) - link it to this one too, otherwise
            // ValidateVehicleChainAsync's "brand must belong to category" check fails it.
            if (existing.Categories != null && existing.Categories.All(c => c.Id != category.Id))
            {
                existing.Categories.Add(category);
                await _context.SaveChangesAsync();
                createdNotes.Add($"podpięto markę '{existing.Name}' pod kategorię '{category.Name}'");
            }
            return existing;
        }

        var brand = new Brand { Name = ClampName(name), Slug = Slugify(name), Categories = new List<VehicleCategory> { category } };
        _context.Brands.Add(brand);
        await _context.SaveChangesAsync();

        brandsByName[key] = brand;
        createdNotes.Add($"utworzono nową markę '{brand.Name}' w kategorii '{category.Name}'");
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
                CategorySlug = (string?)node.Element("Category") ?? string.Empty,
                VehicleSubtypeName = (string?)node.Element("VehicleSubtype"),
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
                ConditionText = (string?)node.Element("Condition"),
                PartCategoryName = (string?)node.Element("PartCategory"),
                PartSubcategoryName = (string?)node.Element("PartSubcategory"),
                CatalogNumber = (string?)node.Element("CatalogNumber"),
                OemNumber = (string?)node.Element("OemNumber"),
                PartManufacturer = (string?)node.Element("PartManufacturer"),
                Compatibility = (string?)node.Element("Compatibility"),
                AxleCount = (int?)node.Element("AxleCount"),
                Payload = (int?)node.Element("Payload"),
                CargoLength = (decimal?)node.Element("CargoLength"),
                CargoHeight = (decimal?)node.Element("CargoHeight"),
                Volume = (decimal?)node.Element("Volume"),
                OperatingWeightKg = (int?)node.Element("OperatingWeightKg"),
                WorkingWidthCm = (int?)node.Element("WorkingWidthCm"),
                MaxDiggingDepthM = (decimal?)node.Element("MaxDiggingDepthM"),
                BucketCapacityL = (int?)node.Element("BucketCapacityL"),
                TankCapacityL = (int?)node.Element("TankCapacityL"),
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
            string? GetOrNull(string col) => string.IsNullOrEmpty(Get(col)) ? null : Get(col);
            int? GetIntOrNull(string col) => int.TryParse(Get(col), out var n) && n > 0 ? n : null;
            decimal? GetDecimalOrNull(string col) => decimal.TryParse(Get(col), out var n) && n > 0 ? n : null;

            decimal.TryParse(Get("price"), out var price);
            int.TryParse(Get("year"), out var year);
            int.TryParse(Get("mileage"), out var mileage);

            var imageUrls = Get("imageurls").Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(u => u.Trim())
                .Where(u => u.Length > 0)
                .ToList();

            items.Add(new PartnerFeedItem
            {
                ExternalId = Get("externalid"),
                Title = Get("title"),
                Description = GetOrNull("description"),
                Price = price,
                CategorySlug = Get("category"),
                VehicleSubtypeName = GetOrNull("vehiclesubtype"),
                BrandName = Get("brand"),
                ModelName = Get("model"),
                Year = year,
                Mileage = mileage,
                FuelTypeName = GetOrNull("fueltype"),
                GearboxName = GetOrNull("gearbox"),
                PowerHP = GetIntOrNull("powerhp"),
                Vin = GetOrNull("vin"),
                City = GetOrNull("city"),
                Region = GetOrNull("region"),
                ConditionText = GetOrNull("condition"),
                PartCategoryName = GetOrNull("partcategory"),
                PartSubcategoryName = GetOrNull("partsubcategory"),
                CatalogNumber = GetOrNull("catalognumber"),
                OemNumber = GetOrNull("oemnumber"),
                PartManufacturer = GetOrNull("partmanufacturer"),
                Compatibility = GetOrNull("compatibility"),
                AxleCount = GetIntOrNull("axlecount"),
                Payload = GetIntOrNull("payload"),
                CargoLength = GetDecimalOrNull("cargolength"),
                CargoHeight = GetDecimalOrNull("cargoheight"),
                Volume = GetDecimalOrNull("volume"),
                OperatingWeightKg = GetIntOrNull("operatingweightkg"),
                WorkingWidthCm = GetIntOrNull("workingwidthcm"),
                MaxDiggingDepthM = GetDecimalOrNull("maxdiggingdepthm"),
                BucketCapacityL = GetIntOrNull("bucketcapacityl"),
                TankCapacityL = GetIntOrNull("tankcapacityl"),
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
        public string CategorySlug { get; set; } = string.Empty;
        public string? VehicleSubtypeName { get; set; }
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
        public string? ConditionText { get; set; }
        public string? PartCategoryName { get; set; }
        public string? PartSubcategoryName { get; set; }
        public string? CatalogNumber { get; set; }
        public string? OemNumber { get; set; }
        public string? PartManufacturer { get; set; }
        public string? Compatibility { get; set; }
        public int? AxleCount { get; set; }
        public int? Payload { get; set; }
        public decimal? CargoLength { get; set; }
        public decimal? CargoHeight { get; set; }
        public decimal? Volume { get; set; }
        public int? OperatingWeightKg { get; set; }
        public int? WorkingWidthCm { get; set; }
        public decimal? MaxDiggingDepthM { get; set; }
        public int? BucketCapacityL { get; set; }
        public int? TankCapacityL { get; set; }
        public List<string> ImageUrls { get; set; } = new();
    }
}
