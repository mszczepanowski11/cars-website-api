using AutoMapper;
using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.DTOs.Advert;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;

public class FavoriteService : IFavoriteService
{
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;

    public FavoriteService(AppDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task AddFavoriteAsync(int userId, int advertId)
    {
        var exists = await _context.FavoriteAdverts
            .AnyAsync(f => f.UserId == userId && f.AdvertId == advertId);
        if (!exists)
        {
            _context.FavoriteAdverts.Add(new FavoriteAdvert { UserId = userId, AdvertId = advertId });
            await _context.SaveChangesAsync();
        }
    }

    public async Task RemoveFavoriteAsync(int userId, int advertId)
    {
        var fav = await _context.FavoriteAdverts
            .FirstOrDefaultAsync(f => f.UserId == userId && f.AdvertId == advertId);
        if (fav != null)
        {
            _context.FavoriteAdverts.Remove(fav);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<PagedResult<CarAdvertResponseDto>> GetUserFavoritesAsync(int userId, int page, int pageSize)
    {
        var favoriteIds = await _context.FavoriteAdverts
            .Where(f => f.UserId == userId)
            .Select(f => f.AdvertId)
            .ToListAsync();

        var query = _context.CarAdverts
            .Include(a => a.Brand).Include(a => a.Model)
            .Include(a => a.Generation).Include(a => a.EngineVersion)
            .Include(a => a.FuelType).Include(a => a.Gearbox)
            .Include(a => a.BodyType).Include(a => a.Images)
            .Include(a => a.AdvertFeatures)
                .ThenInclude(af => af.Feature).ThenInclude(f => f.Category)
            .Where(a => favoriteIds.Contains(a.Id))
            .OrderByDescending(a => a.CreatedAt);

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<CarAdvertResponseDto>
        {
            Items = _mapper.Map<List<CarAdvertResponseDto>>(items),
            TotalCount = total
        };
    }

    public async Task<bool> IsFavoriteAsync(int userId, int advertId) =>
        await _context.FavoriteAdverts.AnyAsync(f => f.UserId == userId && f.AdvertId == advertId);
}