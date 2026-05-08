using AutoMapper;
using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.DTOs.Advert;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;

public class AdvertService : IAdvertService
{
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;

    public AdvertService(AppDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    
    public async Task<int> CreateCarAdvertAsync(CreateCarAdvertDto dto,int userId)
    {
        var advert = _mapper.Map<CarAdvert>(dto);
        advert.CreatedAt = DateTime.UtcNow;
        advert.UserId = userId;
        
        
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

        return advert.Id;
    }
    
    
    
    public async Task UpdateCarAdvertAsync(int id, UpdateCarAdvertDto dto)
    {
        var advert = await _context.CarAdverts
            .Include(a => a.AdvertFeatures)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (advert == null)
            throw new KeyNotFoundException("Advert not found");

        _mapper.Map(dto, advert);

        // Usuń stare cechy
        _context.AdvertFeatures.RemoveRange(advert.AdvertFeatures);

        // Dodaj nowe
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


   
    public async Task DeleteCarAdvertAsync(int id)
    {
        var advert = await _context.CarAdverts.FindAsync(id);
        if (advert == null)
            return;

        _context.CarAdverts.Remove(advert);
        await _context.SaveChangesAsync();
    }

    
    public async Task<CarAdvertResponseDto> GetCarAdvertByIdAsync(int id)
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

        query = dto.SortBy switch
        {
            "price_asc" => query.OrderBy(a => a.Price),
            "price_desc" => query.OrderByDescending(a => a.Price),
            "year_desc" => query.OrderByDescending(a => a.Year),
            "year_asc" => query.OrderBy(a => a.Year),
            _ => query.OrderByDescending(a => a.CreatedAt)
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
}
