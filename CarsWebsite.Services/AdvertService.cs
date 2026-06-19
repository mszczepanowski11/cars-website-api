using AutoMapper;
using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.DTOs.Advert;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public class AdvertService : IAdvertService
{
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;
    private readonly ILogger<AdvertService> _logger;

    public AdvertService(AppDbContext context, IMapper mapper, ILogger<AdvertService> logger)
    {
        _context = context;
        _mapper = mapper;
        _logger = logger;
    }

    
    public async Task<int> CreateCarAdvertAsync(CreateCarAdvertDto dto,int userId)
    {
        var advert = _mapper.Map<CarAdvert>(dto);
        advert.CreatedAt = DateTime.UtcNow;
        advert.UserId = userId;
        advert.ExpiresAt = DateTime.UtcNow.AddDays(30);
        
        
        _context.CarAdverts.Add(advert);
        await _context.SaveChangesAsync();

        
        if (dto.FeatureIds != null && dto.FeatureIds.Any())
        {
            var features = dto.FeatureIds.Select(fid => new AdvertFeature
            {
                AdvertId = advert.Id,
                FeatureId = fid
            });

            _context.AdvertFeatures.AddRange(features);
            await _context.SaveChangesAsync();
        }

        // Generate URL slug from ID + title
        var slugBase = $"{advert.Id}-{advert.Title.ToLowerInvariant()}";
        var slugClean = System.Text.RegularExpressions.Regex.Replace(slugBase, @"[^a-z0-9\-]", "-");
        var slug = System.Text.RegularExpressions.Regex.Replace(slugClean, @"-{2,}", "-").Trim('-');
        if (slug.Length > 80) slug = slug[..80].TrimEnd('-');
        advert.Slug = slug;
        await _context.SaveChangesAsync();

        return advert.Id;
    }
    
    
    
    public async Task UpdateCarAdvertAsync(int id, UpdateCarAdvertDto dto, int userId)
    {
        var advert = await _context.CarAdverts
            .Include(a => a.AdvertFeatures)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (advert == null)
            throw new KeyNotFoundException("Advert not found");

        if (advert.UserId != userId)    
            throw new UnauthorizedAccessException("You do not own this advert");

        _mapper.Map(dto, advert);

        _context.AdvertFeatures.RemoveRange(advert.AdvertFeatures);

        if (dto.FeatureIds != null && dto.FeatureIds.Any())
        {
            var newFeatures = dto.FeatureIds.Select(fid => new AdvertFeature
            {
                AdvertId = advert.Id,
                FeatureId = fid
            });

            _context.AdvertFeatures.AddRange(newFeatures);
        }

        await _context.SaveChangesAsync();
    }


   
    public async Task DeleteCarAdvertAsync(int id, int userId)
    {
        var advert = await _context.CarAdverts.FindAsync(id);
        if (advert == null)
            return;

        if (advert.UserId != userId)
            throw new UnauthorizedAccessException("You do not own this advert");

        advert.IsActive = false;
        advert.IsHidden = true;
        advert.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    
    public async Task<CarAdvertResponseDto> GetCarAdvertByIdAsync(int id, int? requestingUserId = null, bool isAdmin = false)
    {
        var advert = await _context.CarAdverts
            .Include(a => a.Brand)
            .Include(a => a.Model)
            .Include(a => a.Generation)
            .Include(a => a.EngineVersion)
            .Include(a => a.FuelType)
            .Include(a => a.Gearbox)
            .Include(a => a.BodyType)
            .Include(a => a.Images)
            .Include(a => a.AdvertFeatures)
                .ThenInclude(af => af.Feature)
                    .ThenInclude(f => f.Category)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (advert == null)
            throw new KeyNotFoundException("Advert not found");

        if (isAdmin) return _mapper.Map<CarAdvertResponseDto>(advert);
        if (advert.IsHidden || !advert.IsActive)
        {
            if (requestingUserId == null || advert.UserId != requestingUserId)
                throw new KeyNotFoundException("Advert not found");
        }

        return _mapper.Map<CarAdvertResponseDto>(advert);
    }

    
    public async Task<PagedResult<CarAdvertResponseDto>> SearchCarAdvertsAsync(SearchCarAdvertDto dto)
    {
        var query = _context.CarAdverts
            .Include(a => a.Brand)
            .Include(a => a.Model)
            .Include(a => a.Generation)
            .Include(a => a.EngineVersion)
            .Include(a => a.FuelType)
            .Include(a => a.Gearbox)
            .Include(a => a.BodyType)
            .Include(a => a.Images)
            .Include(a => a.AdvertFeatures)
                .ThenInclude(af => af.Feature)
            .AsQueryable();

        query = query.Where(a => a.IsActive && !a.IsHidden);

        if (dto.BrandId.HasValue)
            query = query.Where(a => a.BrandId == dto.BrandId);

        if (dto.ModelId.HasValue)
            query = query.Where(a => a.ModelId == dto.ModelId);

        if (dto.GenerationId.HasValue)
            query = query.Where(a => a.GenerationId == dto.GenerationId);

        if (dto.EngineVersionId.HasValue)
            query = query.Where(a => a.EngineVersionId == dto.EngineVersionId);

        if (dto.FuelTypeId.HasValue)
            query = query.Where(a => a.FuelTypeId == dto.FuelTypeId);

        if (dto.GearboxId.HasValue)
            query = query.Where(a => a.GearboxId == dto.GearboxId);

        if (dto.BodyTypeId.HasValue)
            query = query.Where(a => a.BodyTypeId == dto.BodyTypeId);

        if (dto.YearFrom.HasValue)
            query = query.Where(a => a.Year >= dto.YearFrom);

        if (dto.YearTo.HasValue)
            query = query.Where(a => a.Year <= dto.YearTo);

        if (dto.MileageFrom.HasValue)
            query = query.Where(a => a.Mileage >= dto.MileageFrom);

        if (dto.MileageTo.HasValue)
            query = query.Where(a => a.Mileage <= dto.MileageTo);

        if (dto.PriceFrom.HasValue)
            query = query.Where(a => a.Price >= dto.PriceFrom);

        if (dto.PriceTo.HasValue)
            query = query.Where(a => a.Price <= dto.PriceTo);

        if (dto.FeatureIds != null && dto.FeatureIds.Any())
        {
            query = query.Where(a =>
                dto.FeatureIds.All(fid =>
                    a.AdvertFeatures.Any(af => af.FeatureId == fid)));
        }
        
        if (dto.CategoryId.HasValue)
            query = query.Where(a => a.VehicleCategoryId == dto.CategoryId);

        if (!string.IsNullOrWhiteSpace(dto.TextSearch))
            query = query.Where(a =>
                a.Title.Contains(dto.TextSearch) || a.Description.Contains(dto.TextSearch));

        if (!string.IsNullOrWhiteSpace(dto.City))
            query = query.Where(a => a.City != null && a.City.Contains(dto.City));

        if (!string.IsNullOrWhiteSpace(dto.Region))
            query = query.Where(a => a.Region != null && a.Region.Contains(dto.Region));

        if (dto.DriveTypeId.HasValue)
            query = query.Where(a => a.DriveTypeId == dto.DriveTypeId);

        if (dto.ColorId.HasValue)
            query = query.Where(a => a.ColorId == dto.ColorId);

        if (dto.PowerFrom.HasValue)
            query = query.Where(a => a.PowerHP >= dto.PowerFrom);

        if (dto.PowerTo.HasValue)
            query = query.Where(a => a.PowerHP <= dto.PowerTo);

        if (dto.EngineSizeFrom.HasValue)
            query = query.Where(a => a.EngineSize >= dto.EngineSizeFrom);

        if (dto.EngineSizeTo.HasValue)
            query = query.Where(a => a.EngineSize <= dto.EngineSizeTo);

        if (dto.DoorCount.HasValue)
            query = query.Where(a => a.DoorCount == dto.DoorCount);

        if (dto.SeatsCount.HasValue)
            query = query.Where(a => a.SeatsCount == dto.SeatsCount);

        if (!string.IsNullOrWhiteSpace(dto.Condition))
            query = query.Where(a => a.Condition == dto.Condition);

        if (!string.IsNullOrWhiteSpace(dto.SellerType))
            query = query.Where(a => a.SellerType == dto.SellerType);

        if (dto.IsNegotiable.HasValue)
            query = query.Where(a => a.IsNegotiable == dto.IsNegotiable);

        if (dto.HasDamage.HasValue)
            query = query.Where(a => a.HasDamage == dto.HasDamage);

        if (dto.HasWarranty.HasValue)
            query = query.Where(a => a.HasWarranty == dto.HasWarranty);

        if (dto.HasServiceBook.HasValue)
            query = query.Where(a => a.HasServiceBook == dto.HasServiceBook);

        if (dto.IsImported.HasValue)
            query = query.Where(a => a.IsImported == dto.IsImported);

        if (!string.IsNullOrWhiteSpace(dto.EuroNorm))
            query = query.Where(a => a.EuroNorm == dto.EuroNorm);

        if (dto.AxleCount.HasValue)
            query = query.Where(a => a.AxleCount == dto.AxleCount);

        if (dto.PayloadFrom.HasValue)
            query = query.Where(a => a.Payload >= dto.PayloadFrom);

        if (dto.PayloadTo.HasValue)
            query = query.Where(a => a.Payload <= dto.PayloadTo);

        if (dto.GrossWeightFrom.HasValue)
            query = query.Where(a => a.GrossWeight >= dto.GrossWeightFrom);

        if (dto.GrossWeightTo.HasValue)
            query = query.Where(a => a.GrossWeight <= dto.GrossWeightTo);

        if (!string.IsNullOrWhiteSpace(dto.BodySubtype))
            query = query.Where(a => a.BodySubtype == dto.BodySubtype);

        if (dto.HasRetarder.HasValue)
            query = query.Where(a => a.HasRetarder == dto.HasRetarder);

        if (dto.HasTachograph.HasValue)
            query = query.Where(a => a.HasTachograph == dto.HasTachograph);

        var prioritized = query.OrderBy(a =>
            a.Badge == "TOP" ? 0 : a.Badge == "PREMIUM" ? 1 : a.Badge == "FEATURED" ? 2 : 3);

        query = dto.SortBy switch
        {
            "price_asc"  => prioritized.ThenBy(a => a.Price),
            "price_desc" => prioritized.ThenByDescending(a => a.Price),
            "year_desc"  => prioritized.ThenByDescending(a => a.Year),
            "year_asc"   => prioritized.ThenBy(a => a.Year),
            _            => prioritized.ThenByDescending(a => a.UpdatedAt ?? a.CreatedAt)
        };

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((dto.Page - 1) * dto.PageSize)
            .Take(dto.PageSize)
            .ToListAsync();

        var mapped = _mapper.Map<List<CarAdvertResponseDto>>(items);

        return new PagedResult<CarAdvertResponseDto>
        {
            Items = mapped,
            TotalCount = totalCount
        };
    }

    public async Task<PagedResult<CarAdvertResponseDto>> GetUserAdvertsAsync(int userId, int page = 1, int pageSize = 20)
    {
        var query = _context.CarAdverts
            .Include(a => a.Brand).Include(a => a.Model)
            .Include(a => a.Generation).Include(a => a.EngineVersion)
            .Include(a => a.FuelType).Include(a => a.Gearbox)
            .Include(a => a.BodyType).Include(a => a.Images)
            .Include(a => a.AdvertFeatures).ThenInclude(af => af.Feature)
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt);
        
       
        var totalCount= await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        
        return new PagedResult<CarAdvertResponseDto>
        {
            Items = _mapper.Map<List<CarAdvertResponseDto>>(items),
            TotalCount = totalCount
        };
    }

    public async Task PromoteAdvertAsync(int advertId, int userId, string type, int durationDays)
    {
        var advert = await _context.CarAdverts.FindAsync(advertId)
            ?? throw new KeyNotFoundException("Advert not found.");

        if (advert.UserId != userId)
            throw new UnauthorizedAccessException("You do not own this advert.");

        advert.Badge = type;
        advert.BadgeExpiresAt = DateTime.UtcNow.AddDays(durationDays);
        await _context.SaveChangesAsync();
    }

    public async Task<CarAdvertResponseDto?> GetByVinAsync(string vin)
    {
        var advert = await _context.CarAdverts
            .Include(a => a.Brand).Include(a => a.Model)
            .Include(a => a.Generation).Include(a => a.EngineVersion)
            .Include(a => a.FuelType).Include(a => a.Gearbox)
            .Include(a => a.BodyType).Include(a => a.Images)
            .Include(a => a.AdvertFeatures).ThenInclude(af => af.Feature)
            .FirstOrDefaultAsync(a => a.Vin == vin);

        return advert == null ? null : _mapper.Map<CarAdvertResponseDto>(advert);
    }

    public async Task MarkAsSoldAsync(int advertId, int userId)
    {
        var advert = await _context.CarAdverts.FindAsync(advertId)
            ?? throw new KeyNotFoundException("Advert not found.");
        if (advert.UserId != userId)
            throw new UnauthorizedAccessException("You do not own this advert.");
        advert.SoldAt = DateTime.UtcNow;
        advert.IsActive = false;
        advert.IsHidden = true;
        advert.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task PublishAsync(int advertId, int userId)
    {
        _logger.LogInformation("[Publish] advertId={AdvertId} userId={UserId}", advertId, userId);
        var advert = await _context.CarAdverts.FindAsync(advertId);
        if (advert == null)
        {
            var existsInBase = await _context.Adverts.AnyAsync(a => a.Id == advertId);
            _logger.LogWarning("[Publish] advert {AdvertId} not found in CarAdverts. existsInAdverts={Exists}", advertId, existsInBase);
            throw new KeyNotFoundException("Advert not found.");
        }
        if (advert.UserId != userId)
            throw new UnauthorizedAccessException("You do not own this advert.");
        advert.IsActive = true;
        advert.IsHidden = false;
        advert.SoldAt = null;
        advert.ExpiresAt = DateTime.UtcNow.AddDays(30);
        advert.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task<(int activeCount, int yearCount)> GetPersonalAdCountsAsync(int userId)
    {
        var yearStart = new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var yearEnd   = yearStart.AddYears(1);

        var activeCount = await _context.CarAdverts
            .CountAsync(a => a.UserId == userId && a.IsActive && !a.IsHidden);

        var yearCount = await _context.CarAdverts
            .CountAsync(a => a.UserId == userId && a.CreatedAt >= yearStart && a.CreatedAt < yearEnd);

        return (activeCount, yearCount);
    }
}
