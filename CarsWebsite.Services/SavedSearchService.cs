using System.Text.Json;
using CarsWebsite;
using cars_website_api.CarsWebsite.DTOs.Advert;
using cars_website_api.CarsWebsite.DTOs.SavedSearch;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.EntityFrameworkCore;

public class SavedSearchService : ISavedSearchService
{
    private const int MaxSavedSearchesPerUser = 20;

    private readonly AppDbContext _context;

    public SavedSearchService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<SavedSearchResponseDto>> GetMyAsync(int userId, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.SavedSearches
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt);

        var totalCount = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<SavedSearchResponseDto> { Items = items.Select(MapToDto).ToList(), TotalCount = totalCount };
    }

    public async Task<SavedSearchResponseDto> CreateAsync(int userId, CreateSavedSearchDto dto)
    {
        var count = await _context.SavedSearches.CountAsync(s => s.UserId == userId);
        if (count >= MaxSavedSearchesPerUser)
            throw new InvalidOperationException($"Możesz zapisać maksymalnie {MaxSavedSearchesPerUser} wyszukiwań.");

        var entity = new SavedSearch
        {
            UserId = userId,
            Name = dto.Name.Trim(),
            CriteriaJson = JsonSerializer.Serialize(dto.Criteria),
            NotifyOnNew = dto.NotifyOnNew,
            CreatedAt = DateTime.UtcNow,
            LastCheckedAt = DateTime.UtcNow,
        };

        _context.SavedSearches.Add(entity);
        await _context.SaveChangesAsync();

        return MapToDto(entity);
    }

    public async Task<SavedSearchResponseDto> UpdateAsync(int id, int userId, UpdateSavedSearchDto dto)
    {
        var entity = await _context.SavedSearches.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId)
            ?? throw new KeyNotFoundException("Zapisane wyszukiwanie nie istnieje.");

        if (!string.IsNullOrWhiteSpace(dto.Name)) entity.Name = dto.Name.Trim();
        if (dto.Criteria != null) entity.CriteriaJson = JsonSerializer.Serialize(dto.Criteria);
        if (dto.NotifyOnNew.HasValue) entity.NotifyOnNew = dto.NotifyOnNew.Value;

        await _context.SaveChangesAsync();
        return MapToDto(entity);
    }

    public async Task DeleteAsync(int id, int userId)
    {
        var entity = await _context.SavedSearches.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId)
            ?? throw new KeyNotFoundException("Zapisane wyszukiwanie nie istnieje.");

        _context.SavedSearches.Remove(entity);
        await _context.SaveChangesAsync();
    }

    public async Task SetNotifyAsync(int id, int userId, bool notifyOnNew)
    {
        var entity = await _context.SavedSearches.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId)
            ?? throw new KeyNotFoundException("Zapisane wyszukiwanie nie istnieje.");

        entity.NotifyOnNew = notifyOnNew;
        await _context.SaveChangesAsync();
    }

    private static SavedSearchResponseDto MapToDto(SavedSearch s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Criteria = JsonSerializer.Deserialize<SearchCarAdvertDto>(s.CriteriaJson) ?? new SearchCarAdvertDto(),
        CreatedAt = s.CreatedAt,
        NotifyOnNew = s.NotifyOnNew,
        NewResultsCount = s.NewResultsCount,
    };
}
