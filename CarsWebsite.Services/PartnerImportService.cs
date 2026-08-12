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

    public int CountFeedItems(string content, PartnerFeedFormat format)
    {
        // Item boundaries (<Advert> elements / CSV rows) are the same regardless of field naming,
        // so this doesn't need field-mapping awareness - and can't have any yet anyway, since it's
        // used by the signup preview before a Partner row (and its mappings) exist.
        var items = format == PartnerFeedFormat.Xml ? ParseXml(content, null) : ParseCsv(content, null);
        return items.Count;
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

        // Schema adapter layer (CTO audit Etap 2): a partner's own field names ("CenaNetto" instead
        // of "Price") and taxonomy strings ("Osobowe" instead of the "auta-osobowe" slug) rarely
        // match CARIZO's schema exactly - this used to mean writing new parsing code per
        // integrator. An empty mapping set (the default for every partner today) falls back
        // exactly to CARIZO's own field names, so this is fully backward compatible.
        var fieldMap = (await _context.PartnerFieldMappings.Where(m => m.PartnerId == partner.Id).ToListAsync())
            .ToDictionary(m => m.OurField, m => m.SourcePath);
        var valueMap = (await _context.PartnerValueMappings.Where(m => m.PartnerId == partner.Id).ToListAsync())
            .ToDictionary(m => (m.Field, m.ExternalValue.Trim().ToLowerInvariant()), m => m.InternalValue);

        var errors = new List<string>();
        List<PartnerFeedItem> items;
        try
        {
            items = format == PartnerFeedFormat.Xml ? ParseXml(content, fieldMap) : ParseCsv(content, fieldMap);
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
                var categoryKey = MapValue(valueMap, "Category", item.CategorySlug);
                if (!categoriesBySlug.TryGetValue(categoryKey.Trim().ToLowerInvariant(), out var category))
                    throw new ArgumentException($"nieznana kategoria '{item.CategorySlug}'");

                await ImportItemAsync(item, partner, category, valueMap, brandsByName, fuelTypesByName, gearboxesByName,
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
        Dictionary<(string Field, string ExternalValue), string> valueMap,
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
            var brandKey = MapValue(valueMap, "Brand", item.BrandName);
            brand = await GetOrCreateBrandAsync(brandKey, category, brandsByName, createdNotes);

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
            fuelType = await GetOrCreateFuelTypeAsync(MapValue(valueMap, "FuelType", item.FuelTypeName), fuelTypesByName, createdNotes);

        Gearbox? gearbox = null;
        if (!string.IsNullOrWhiteSpace(item.GearboxName))
            gearbox = await GetOrCreateGearboxAsync(MapValue(valueMap, "Gearbox", item.GearboxName), gearboxesByName, createdNotes);

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

        var mappedCondition = string.IsNullOrWhiteSpace(item.ConditionText) ? item.ConditionText : MapValue(valueMap, "Condition", item.ConditionText);
        var condition = mappedCondition?.Trim().ToLowerInvariant() == "new" ? "new" : "used";

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
                Description = item.Description ?? string.Empty,
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

            var duplicate = await DetectDuplicateAsync(advert, brand, model);
            if (duplicate != null)
            {
                advert.DuplicateOfId = duplicate.Value.CanonicalId;
                advert.DuplicateMatchReason = duplicate.Value.Reason;
                createdNotes.Add($"oznaczono jako duplikat ogłoszenia #{duplicate.Value.CanonicalId} ({duplicate.Value.Reason})");
            }

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
                Description = item.Description ?? string.Empty,
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

            // CTO audit finding (CRITICAL): UpdateCarAdvertDto has no ExpiresAt field, so
            // UpdateCarAdvertAsync never touches it - ExpiresAt is set once at CreateCarAdvertAsync
            // and ExpiryReminderJob deactivates any advert past it, regardless of source. Without
            // this, a partner advert present in EVERY sync (i.e. still genuinely for sale) would
            // still silently deactivate 90 days after its original creation date, since nothing
            // else ever pushes ExpiresAt forward. Every successful sync of an existing item is
            // exactly the confirmation that this listing is still live - renew it the same way
            // AdvertService.PublishAsync renews a manually-republished advert (flat 90 days).
            existing.ExpiresAt = DateTime.UtcNow.AddDays(90);
            await _context.SaveChangesAsync();

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

    // Cross-source duplicate detection (CTO audit Etap 2): "AKOL i 44FOX prawdopodobnie odsprzedają
    // nakładający się inwentarz dealerski" - the same physical car can arrive from two different
    // partners (or a partner and a manually-created listing). Called only right after a NEW advert
    // is created during import (not on update - an already-linked ExternalId is by definition the
    // same listing being refreshed, not a new duplicate candidate).
    private async Task<(int CanonicalId, string Reason)?> DetectDuplicateAsync(CarAdvert advert, Brand? brand, Model? model)
    {
        // VIN-first: CreateCarAdvertAsync already normalizes VIN to uppercase before saving (see
        // AdvertService.CreateCarAdvertAsync), so a plain equality comparison is safe here. Matches
        // only against non-duplicate ("canonical") adverts and picks the oldest one, so repeated
        // imports build a flat star shape (everything points at the original) rather than a chain.
        if (!string.IsNullOrWhiteSpace(advert.Vin))
        {
            var vinMatch = await _context.CarAdverts
                .Where(a => a.Id != advert.Id && a.Vin == advert.Vin && a.IsActive && a.DuplicateOfId == null)
                .OrderBy(a => a.CreatedAt)
                .FirstOrDefaultAsync();
            if (vinMatch != null) return (vinMatch.Id, "VIN");
        }

        // Fuzzy fallback: only meaningful once brand+model+year actually resolved - a parts/
        // accessories/machinery listing has no comparable signal here. Deliberately conservative:
        // only auto-flags when EXACTLY one candidate matches, to avoid guessing which of several
        // genuinely different cars of the same brand/model/year is the "real" duplicate.
        if (brand == null || model == null || advert.Year <= 0 || advert.Price <= 0) return null;

        var priceLow = advert.Price * 0.95m;
        var priceHigh = advert.Price * 1.05m;
        var mileageLow = (int)(advert.Mileage * 0.9);
        var mileageHigh = (int)(advert.Mileage * 1.1);

        var candidates = await _context.CarAdverts
            .Where(a => a.Id != advert.Id && a.IsActive && a.DuplicateOfId == null
                && a.BrandId == brand.Id && a.ModelId == model.Id && a.Year == advert.Year
                && a.Price >= priceLow && a.Price <= priceHigh
                && a.Mileage >= mileageLow && a.Mileage <= mileageHigh)
            .OrderBy(a => a.CreatedAt)
            .Take(2)
            .ToListAsync();

        return candidates.Count == 1
            ? (candidates[0].Id, "Fuzzy: marka+model+rok, cena ±5%, przebieg ±10%")
            : null;
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

    // Value-mapping lookup: an unmapped raw value passes through unchanged (falls to the existing
    // exact-name matching/auto-create logic), exactly like an empty fieldMap falls back to
    // CARIZO's own element/column names below.
    private static string MapValue(Dictionary<(string Field, string ExternalValue), string> valueMap, string field, string rawValue)
        => valueMap.TryGetValue((field, rawValue.Trim().ToLowerInvariant()), out var mapped) ? mapped : rawValue;

    // A partner's own element name for OurField, or CARIZO's own default (the literal field name)
    // when nothing is mapped - so an empty fieldMap reproduces today's fixed-schema behavior exactly.
    private static string XmlName(Dictionary<string, string>? fieldMap, string ourField)
        => fieldMap != null && fieldMap.TryGetValue(ourField, out var mapped) ? mapped : ourField;

    private static List<PartnerFeedItem> ParseXml(string content, Dictionary<string, string>? fieldMap)
    {
        var doc = XDocument.Parse(content);
        var items = new List<PartnerFeedItem>();
        string N(string ourField) => XmlName(fieldMap, ourField);

        foreach (var node in doc.Descendants("Advert"))
        {
            items.Add(new PartnerFeedItem
            {
                ExternalId = (string?)node.Element(N("ExternalId")) ?? string.Empty,
                Title = (string?)node.Element(N("Title")) ?? string.Empty,
                Description = (string?)node.Element(N("Description")),
                Price = (decimal?)node.Element(N("Price")) ?? 0,
                CategorySlug = (string?)node.Element(N("Category")) ?? string.Empty,
                VehicleSubtypeName = (string?)node.Element(N("VehicleSubtype")),
                BrandName = (string?)node.Element(N("Brand")) ?? string.Empty,
                ModelName = (string?)node.Element(N("Model")) ?? string.Empty,
                Year = (int?)node.Element(N("Year")) ?? 0,
                Mileage = (int?)node.Element(N("Mileage")) ?? 0,
                FuelTypeName = (string?)node.Element(N("FuelType")),
                GearboxName = (string?)node.Element(N("Gearbox")),
                PowerHP = (int?)node.Element(N("PowerHP")),
                Vin = (string?)node.Element(N("Vin")),
                City = (string?)node.Element(N("City")),
                Region = (string?)node.Element(N("Region")),
                ConditionText = (string?)node.Element(N("Condition")),
                PartCategoryName = (string?)node.Element(N("PartCategory")),
                PartSubcategoryName = (string?)node.Element(N("PartSubcategory")),
                CatalogNumber = (string?)node.Element(N("CatalogNumber")),
                OemNumber = (string?)node.Element(N("OemNumber")),
                PartManufacturer = (string?)node.Element(N("PartManufacturer")),
                Compatibility = (string?)node.Element(N("Compatibility")),
                AxleCount = (int?)node.Element(N("AxleCount")),
                Payload = (int?)node.Element(N("Payload")),
                CargoLength = (decimal?)node.Element(N("CargoLength")),
                CargoHeight = (decimal?)node.Element(N("CargoHeight")),
                Volume = (decimal?)node.Element(N("Volume")),
                OperatingWeightKg = (int?)node.Element(N("OperatingWeightKg")),
                WorkingWidthCm = (int?)node.Element(N("WorkingWidthCm")),
                MaxDiggingDepthM = (decimal?)node.Element(N("MaxDiggingDepthM")),
                BucketCapacityL = (int?)node.Element(N("BucketCapacityL")),
                TankCapacityL = (int?)node.Element(N("TankCapacityL")),
                // Images list is not field-mappable (see PartnerFieldMapping.FieldNames) - always
                // CARIZO's own Images/Image container/child names.
                ImageUrls = node.Element("Images")?.Elements("Image")
                    .Select(e => e.Value.Trim())
                    .Where(v => v.Length > 0)
                    .ToList() ?? new List<string>(),
            });
        }

        return items;
    }

    private static List<PartnerFeedItem> ParseCsv(string content, Dictionary<string, string>? fieldMap)
    {
        var lines = content.Replace("\r\n", "\n").Replace("\r", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return new List<PartnerFeedItem>();

        var header = SplitCsvLine(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
        var items = new List<PartnerFeedItem>();
        // CARIZO's own default column name for OurField is simply its lowercase form
        // ("PowerHP" -> "powerhp") - see PartnerFieldMapping's comment for why this holds for
        // every mappable field.
        string N(string ourField) => (fieldMap != null && fieldMap.TryGetValue(ourField, out var mapped) ? mapped : ourField).Trim().ToLowerInvariant();

        for (var i = 1; i < lines.Length; i++)
        {
            var fields = SplitCsvLine(lines[i]);
            string Get(string ourField)
            {
                var idx = header.IndexOf(N(ourField));
                return idx >= 0 && idx < fields.Count ? fields[idx].Trim() : string.Empty;
            }
            string? GetOrNull(string ourField) => string.IsNullOrEmpty(Get(ourField)) ? null : Get(ourField);
            int? GetIntOrNull(string ourField) => int.TryParse(Get(ourField), out var n) && n > 0 ? n : null;
            decimal? GetDecimalOrNull(string ourField) => decimal.TryParse(Get(ourField), out var n) && n > 0 ? n : null;

            decimal.TryParse(Get("Price"), out var price);
            int.TryParse(Get("Year"), out var year);
            int.TryParse(Get("Mileage"), out var mileage);

            // Image list is not field-mappable - always CARIZO's own "imageurls" column.
            var imageUrls = Get("ImageUrls").Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(u => u.Trim())
                .Where(u => u.Length > 0)
                .ToList();

            items.Add(new PartnerFeedItem
            {
                ExternalId = Get("ExternalId"),
                Title = Get("Title"),
                Description = GetOrNull("Description"),
                Price = price,
                CategorySlug = Get("Category"),
                VehicleSubtypeName = GetOrNull("VehicleSubtype"),
                BrandName = Get("Brand"),
                ModelName = Get("Model"),
                Year = year,
                Mileage = mileage,
                FuelTypeName = GetOrNull("FuelType"),
                GearboxName = GetOrNull("Gearbox"),
                PowerHP = GetIntOrNull("PowerHP"),
                Vin = GetOrNull("Vin"),
                City = GetOrNull("City"),
                Region = GetOrNull("Region"),
                ConditionText = GetOrNull("Condition"),
                PartCategoryName = GetOrNull("PartCategory"),
                PartSubcategoryName = GetOrNull("PartSubcategory"),
                CatalogNumber = GetOrNull("CatalogNumber"),
                OemNumber = GetOrNull("OemNumber"),
                PartManufacturer = GetOrNull("PartManufacturer"),
                Compatibility = GetOrNull("Compatibility"),
                AxleCount = GetIntOrNull("AxleCount"),
                Payload = GetIntOrNull("Payload"),
                CargoLength = GetDecimalOrNull("CargoLength"),
                CargoHeight = GetDecimalOrNull("CargoHeight"),
                Volume = GetDecimalOrNull("Volume"),
                OperatingWeightKg = GetIntOrNull("OperatingWeightKg"),
                WorkingWidthCm = GetIntOrNull("WorkingWidthCm"),
                MaxDiggingDepthM = GetDecimalOrNull("MaxDiggingDepthM"),
                BucketCapacityL = GetIntOrNull("BucketCapacityL"),
                TankCapacityL = GetIntOrNull("TankCapacityL"),
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
