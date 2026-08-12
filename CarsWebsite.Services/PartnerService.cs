using System.Security.Cryptography;
using CarsWebsite;
using cars_website_api.CarsWebsite.DTOs.Partner;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.EntityFrameworkCore;

public class PartnerService : IPartnerService
{
    private readonly AppDbContext _context;
    private readonly IPartnerFeedFetchService _feedFetch;
    private readonly IPartnerImportService _partnerImport;

    public PartnerService(AppDbContext context, IPartnerFeedFetchService feedFetch, IPartnerImportService partnerImport)
    {
        _context = context;
        _feedFetch = feedFetch;
        _partnerImport = partnerImport;
    }

    public async Task<List<PartnerResponseDto>> GetAllAsync()
    {
        var partners = await _context.Partners
            .Include(p => p.LinkedUser)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        return partners.Select(MapToDto).ToList();
    }

    public async Task<PartnerResponseDto> GetByIdAsync(int id)
    {
        var partner = await _context.Partners.Include(p => p.LinkedUser).FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new KeyNotFoundException("Partner nie istnieje.");
        return MapToDto(partner);
    }

    public async Task<(PartnerResponseDto Partner, string ApiKey)> CreateAsync(CreatePartnerDto dto)
    {
        var linkedUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == dto.LinkedUserId)
            ?? throw new KeyNotFoundException("Wskazany użytkownik nie istnieje.");
        if (linkedUser.AccountType != AccountType.Business)
            throw new ArgumentException("Konto powiązane z partnerem musi być kontem firmowym.");

        var apiKey = GenerateApiKey();
        var entity = new Partner
        {
            CompanyName = dto.CompanyName.Trim(),
            ContactEmail = dto.ContactEmail.Trim(),
            ApiKeyHash = BCrypt.Net.BCrypt.HashPassword(apiKey),
            LinkedUserId = dto.LinkedUserId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            FeedUrl = string.IsNullOrWhiteSpace(dto.FeedUrl) ? null : dto.FeedUrl.Trim(),
            FeedFormat = dto.FeedFormat,
            AutoSyncEnabled = dto.AutoSyncEnabled,
        };

        _context.Partners.Add(entity);
        await _context.SaveChangesAsync();

        entity.LinkedUser = linkedUser;
        return (MapToDto(entity), apiKey);
    }

    public async Task<PartnerResponseDto> UpdateAsync(int id, UpdatePartnerDto dto)
    {
        var entity = await _context.Partners.Include(p => p.LinkedUser).FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new KeyNotFoundException("Partner nie istnieje.");

        entity.CompanyName = dto.CompanyName.Trim();
        entity.ContactEmail = dto.ContactEmail.Trim();
        entity.IsActive = dto.IsActive;
        entity.FeedUrl = string.IsNullOrWhiteSpace(dto.FeedUrl) ? null : dto.FeedUrl.Trim();
        entity.FeedFormat = dto.FeedFormat;
        entity.AutoSyncEnabled = dto.AutoSyncEnabled;

        await _context.SaveChangesAsync();
        return MapToDto(entity);
    }

    public async Task<PartnerImportLogResponseDto> SyncNowAsync(int id)
    {
        var partner = await _context.Partners.FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new KeyNotFoundException("Partner nie istnieje.");
        if (string.IsNullOrWhiteSpace(partner.FeedUrl))
            throw new InvalidOperationException("Partner nie ma skonfigurowanego adresu feedu (FeedUrl) - synchronizacja ręczna wymaga pull-feedu.");

        var fetch = await _feedFetch.FetchAsync(partner.FeedUrl);
        if (!fetch.Success)
            throw new InvalidOperationException($"Pobranie feedu nie powiodło się: {fetch.Error}");

        return await _partnerImport.ImportAsync(partner, fetch.Content!, fetch.Format);
    }

    public async Task<string> RegenerateApiKeyAsync(int id)
    {
        var entity = await _context.Partners.FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new KeyNotFoundException("Partner nie istnieje.");

        var apiKey = GenerateApiKey();
        entity.ApiKeyHash = BCrypt.Net.BCrypt.HashPassword(apiKey);
        await _context.SaveChangesAsync();

        return apiKey;
    }

    public async Task DeleteAsync(int id)
    {
        var entity = await _context.Partners.FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new KeyNotFoundException("Partner nie istnieje.");

        _context.Partners.Remove(entity);
        await _context.SaveChangesAsync();
    }

    public async Task<List<PartnerImportLogResponseDto>> GetImportLogsAsync(int id, int limit = 20)
    {
        limit = Math.Clamp(limit, 1, 100);
        var logs = await _context.PartnerImportLogs
            .Where(l => l.PartnerId == id)
            .OrderByDescending(l => l.StartedAt)
            .Take(limit)
            .ToListAsync();

        return logs.Select(MapLogToDto).ToList();
    }

    public async Task<Partner?> AuthenticateAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        // BCrypt hashes aren't lookup-able by value, so every active partner's hash must be
        // checked - the partner count is small (business-integration scale, not per-user scale),
        // so this stays cheap.
        var activePartners = await _context.Partners.Where(p => p.IsActive).ToListAsync();
        foreach (var partner in activePartners)
        {
            if (BCrypt.Net.BCrypt.Verify(apiKey, partner.ApiKeyHash))
                return partner;
        }

        return null;
    }

    private static string GenerateApiKey() => "carizo_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static PartnerResponseDto MapToDto(Partner p) => new()
    {
        Id = p.Id,
        CompanyName = p.CompanyName,
        ContactEmail = p.ContactEmail,
        LinkedUserId = p.LinkedUserId,
        LinkedUserEmail = p.LinkedUser?.Email ?? string.Empty,
        IsActive = p.IsActive,
        CreatedAt = p.CreatedAt,
        LastImportAt = p.LastImportAt,
        FeedUrl = p.FeedUrl,
        FeedFormat = p.FeedFormat?.ToString(),
        AutoSyncEnabled = p.AutoSyncEnabled,
    };

    private static PartnerImportLogResponseDto MapLogToDto(PartnerImportLog l) => new()
    {
        Id = l.Id,
        PartnerId = l.PartnerId,
        Format = l.Format.ToString(),
        StartedAt = l.StartedAt,
        CompletedAt = l.CompletedAt,
        ItemsTotal = l.ItemsTotal,
        ItemsCreated = l.ItemsCreated,
        ItemsUpdated = l.ItemsUpdated,
        ItemsFailed = l.ItemsFailed,
        ErrorSummary = l.ErrorSummary,
    };
}
