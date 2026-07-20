using System.Security.Cryptography;
using CarsWebsite;
using cars_website_api.CarsWebsite.DTOs.Partner;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace cars_website_api.CarsWebsite.Services;

// Public self-service "Dla firm" signup: a company fills a short form (no login), we optionally
// fetch+count their feed URL for immediate feedback, then queue a PartnerSignupRequest for admin
// review - approving one is what actually creates the Business account, the Partner row, and runs
// the first import (see ApproveAsync). Nothing here ever touches live data on its own.
public class PartnerSignupService : IPartnerSignupService
{
    private readonly AppDbContext _context;
    private readonly IPartnerFeedFetchService _feedFetch;
    private readonly IPartnerImportService _partnerImport;
    private readonly IEmailService _email;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PartnerSignupService> _logger;

    public PartnerSignupService(
        AppDbContext context,
        IPartnerFeedFetchService feedFetch,
        IPartnerImportService partnerImport,
        IEmailService email,
        IConfiguration configuration,
        ILogger<PartnerSignupService> logger)
    {
        _context = context;
        _feedFetch = feedFetch;
        _partnerImport = partnerImport;
        _email = email;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<PartnerSignupPreviewResultDto> PreviewAsync(PartnerSignupInputDto dto)
    {
        ValidateInput(dto);

        if (string.IsNullOrWhiteSpace(dto.FeedUrl))
            return new PartnerSignupPreviewResultDto { Valid = true, NoFeedProvided = true };

        var fetch = await _feedFetch.FetchAsync(dto.FeedUrl.Trim());
        if (!fetch.Success)
            return new PartnerSignupPreviewResultDto { Valid = false, Error = fetch.Error };

        int count;
        try
        {
            count = _partnerImport.CountFeedItems(fetch.Content!, fetch.Format);
        }
        catch (Exception ex)
        {
            return new PartnerSignupPreviewResultDto { Valid = false, Error = $"Nie udało się odczytać pliku: {ex.Message}" };
        }

        if (count == 0)
            return new PartnerSignupPreviewResultDto { Valid = false, Error = "Plik nie zawiera żadnych ogłoszeń." };

        return new PartnerSignupPreviewResultDto { Valid = true, ItemCount = count, Format = fetch.Format.ToString() };
    }

    public async Task<PartnerSignupResponseDto> SubmitAsync(PartnerSignupInputDto dto)
    {
        ValidateInput(dto);

        var request = new PartnerSignupRequest
        {
            CompanyName = dto.CompanyName.Trim(),
            Email = dto.Email.Trim().ToLowerInvariant(),
            Phone = dto.Phone.Trim(),
            WebsiteUrl = string.IsNullOrWhiteSpace(dto.WebsiteUrl) ? null : dto.WebsiteUrl.Trim(),
            FeedUrl = string.IsNullOrWhiteSpace(dto.FeedUrl) ? null : dto.FeedUrl.Trim(),
            Status = PartnerSignupStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        };

        // Re-validate at submission time rather than trusting the earlier preview call - the
        // two are separate requests and the feed could have changed or the preview skipped.
        if (request.FeedUrl != null)
        {
            var fetch = await _feedFetch.FetchAsync(request.FeedUrl);
            if (fetch.Success)
            {
                request.FeedFormat = fetch.Format;
                try { request.DetectedItemCount = _partnerImport.CountFeedItems(fetch.Content!, fetch.Format); }
                catch { /* leave DetectedItemCount null - admin will see the raw feed at approval time */ }
            }
        }

        _context.PartnerSignupRequests.Add(request);
        await _context.SaveChangesAsync();

        return new PartnerSignupResponseDto { Id = request.Id, Status = request.Status.ToString() };
    }

    public async Task<List<PartnerSignupRequestListDto>> GetAllAsync(string? status)
    {
        var query = _context.PartnerSignupRequests.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PartnerSignupStatus>(status, true, out var parsed))
            query = query.Where(r => r.Status == parsed);

        var requests = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
        return requests.Select(MapToListDto).ToList();
    }

    public async Task<ApprovePartnerSignupResultDto> ApproveAsync(int id, int adminUserId)
    {
        var request = await _context.PartnerSignupRequests.FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new KeyNotFoundException("Zgłoszenie nie istnieje.");
        if (request.Status != PartnerSignupStatus.Pending)
            throw new InvalidOperationException("Zgłoszenie zostało już rozpatrzone.");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
        var wasNewAccount = false;

        if (user == null)
        {
            wasNewAccount = true;
            var (name, surname) = SplitCompanyContactName(request.CompanyName);
            user = new User
            {
                Name = name,
                Surname = surname,
                Email = request.Email,
                PhoneNumber = request.Phone,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))),
                DateOfBirth = DateTime.UtcNow.AddYears(-18),
                EmailVerified = false,
                AccountType = AccountType.Business,
                CompanyName = request.CompanyName,
                CreatedByAdminId = adminUserId,
                CreatedAt = DateTime.UtcNow,
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
        }
        else if (user.IsBlocked)
        {
            throw new InvalidOperationException("Konto z tym adresem e-mail jest zablokowane.");
        }
        else if (user.AccountType != AccountType.Business)
        {
            user.AccountType = AccountType.Business;
            user.CompanyName ??= request.CompanyName;
        }

        var apiKey = "carizo_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var partner = new Partner
        {
            CompanyName = request.CompanyName,
            ContactEmail = request.Email,
            ApiKeyHash = BCrypt.Net.BCrypt.HashPassword(apiKey),
            LinkedUserId = user.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            FeedUrl = request.FeedUrl,
            FeedFormat = request.FeedFormat,
            AutoSyncEnabled = request.FeedUrl != null,
        };
        _context.Partners.Add(partner);
        await _context.SaveChangesAsync();

        // Business Directory link: an approved partner is a confirmed company, so it belongs in the
        // public catalogue (blueprint section 17). If a directory row already matches by normalized
        // name (e.g. seeded earlier), claim it (link + mark verified); otherwise create a fresh one.
        try
        {
            var norm = CarizoId.Normalize(request.CompanyName);
            var existing = await _context.DirectoryCompanies.FirstOrDefaultAsync(d => d.NameNormalized == norm);
            if (existing != null)
            {
                existing.PartnerId = partner.Id;
                existing.Status = "active";
                if (string.IsNullOrEmpty(existing.Email)) existing.Email = request.Email;
                if (string.IsNullOrEmpty(existing.Phone) && !string.IsNullOrEmpty(request.Phone)) existing.Phone = request.Phone;
                if (string.IsNullOrEmpty(existing.Website) && !string.IsNullOrEmpty(request.WebsiteUrl)) existing.Website = request.WebsiteUrl;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                var slugBase = CarizoId.Slugify(request.CompanyName);
                var slug = await _context.DirectoryCompanies.AnyAsync(d => d.Slug == slugBase)
                    ? $"{slugBase}-{Guid.NewGuid().ToString("N")[..6]}" : slugBase;
                _context.DirectoryCompanies.Add(new DirectoryCompany
                {
                    PublicId = CarizoId.New("org", "PL"),
                    Name = request.CompanyName,
                    NameNormalized = norm,
                    Category = "dealerzy",
                    CountryCode = "PL",
                    Email = request.Email,
                    Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone,
                    Website = request.WebsiteUrl,
                    Language = "pl",
                    Status = "active",
                    Source = "partner-signup",
                    PartnerId = partner.Id,
                    Slug = slug,
                });
            }
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Directory linking is a nice-to-have on approval, never a reason to fail the approval
            // (the partner + account are already created and are what actually matters here).
            _logger.LogWarning(ex, "[PartnerSignup] Directory link failed for partner {PartnerId} (non-fatal)", partner.Id);
        }

        request.Status = PartnerSignupStatus.Approved;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewedByAdminId = adminUserId;
        request.PartnerId = partner.Id;

        _context.AdminActionLogs.Add(new AdminActionLog
        {
            AdminUserId = adminUserId,
            ActionType = AdminActionType.ApprovePartnerSignup,
            TargetUserId = user.Id,
            Note = $"Zatwierdzono zgłoszenie partnera '{request.CompanyName}' (#{request.Id}) -> Partner #{partner.Id}",
            PerformedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();

        if (wasNewAccount)
            await SendAccountReadyEmailAsync(user, partner);
        else
            await SendPartnerApprovedEmailAsync(user, partner);

        int? created = null, failed = null;
        if (partner.FeedUrl != null)
        {
            var fetch = await _feedFetch.FetchAsync(partner.FeedUrl);
            if (fetch.Success)
            {
                var log = await _partnerImport.ImportAsync(partner, fetch.Content!, fetch.Format);
                created = log.ItemsCreated;
                failed = log.ItemsFailed;
            }
            else
            {
                _logger.LogWarning("[PartnerSignup] Initial import fetch failed for partner {PartnerId}: {Error}", partner.Id, fetch.Error);
            }
        }

        return new ApprovePartnerSignupResultDto
        {
            PartnerId = partner.Id,
            ApiKey = apiKey,
            WasNewAccount = wasNewAccount,
            ImportedItemsCreated = created,
            ImportedItemsFailed = failed,
        };
    }

    public async Task RejectAsync(int id, int adminUserId, string? reason)
    {
        var request = await _context.PartnerSignupRequests.FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new KeyNotFoundException("Zgłoszenie nie istnieje.");
        if (request.Status != PartnerSignupStatus.Pending)
            throw new InvalidOperationException("Zgłoszenie zostało już rozpatrzone.");

        request.Status = PartnerSignupStatus.Rejected;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewedByAdminId = adminUserId;
        request.RejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        _context.AdminActionLogs.Add(new AdminActionLog
        {
            AdminUserId = adminUserId,
            ActionType = AdminActionType.RejectPartnerSignup,
            Note = $"Odrzucono zgłoszenie partnera '{request.CompanyName}' (#{request.Id})" + (reason != null ? $": {reason}" : ""),
            PerformedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();
    }

    private void ValidateInput(PartnerSignupInputDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.CompanyName)) throw new ArgumentException("Podaj nazwę firmy.");
        if (string.IsNullOrWhiteSpace(dto.Email)) throw new ArgumentException("Podaj adres e-mail.");
        if (string.IsNullOrWhiteSpace(dto.Phone)) throw new ArgumentException("Podaj numer telefonu.");
    }

    // The public form collects a company, not a person's full name - Name gets the company name
    // and Surname stays empty, same convention AdminService.SplitFullName already uses for a
    // single-word input (no space to split on).
    private static (string Name, string Surname) SplitCompanyContactName(string companyName)
        => (companyName.Trim(), string.Empty);

    private async Task SendAccountReadyEmailAsync(User user, Partner partner)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        user.PasswordResetToken = token;
        user.PasswordResetTokenExpires = DateTime.UtcNow.AddDays(14);
        await _context.SaveChangesAsync();

        var siteUrl = _configuration["SiteUrl"] ?? "https://carizo.eu";
        var html = EmailService.BuildHtml(
            "Twoja integracja z CARIZO jest aktywna",
            "Zatwierdziliśmy Twoje zgłoszenie i utworzyliśmy konto firmowe w CARIZO. Kliknij poniższy przycisk, aby ustawić hasło i zarządzać swoimi ogłoszeniami.",
            partner.FeedUrl != null
                ? "Synchronizacja Twojego pliku będzie wykonywana automatycznie - nie musisz nic więcej robić."
                : null,
            $"{siteUrl}/reset-password?token={token}",
            "Ustaw hasło");

        _ = _email.SendAsync(user.Email, "Twoje konto i integracja CARIZO są gotowe", html)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _logger.LogError(t.Exception, "[PartnerSignupService] Wysyłka e-maila powitalnego nie powiodła się dla {Email}", user.Email);
            }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private Task SendPartnerApprovedEmailAsync(User user, Partner partner)
    {
        var siteUrl = _configuration["SiteUrl"] ?? "https://carizo.eu";
        var html = EmailService.BuildHtml(
            "Twoja integracja z CARIZO jest aktywna",
            "Zatwierdziliśmy Twoje zgłoszenie partnerskie i powiązaliśmy je z Twoim istniejącym kontem CARIZO.",
            partner.FeedUrl != null
                ? "Synchronizacja Twojego pliku będzie wykonywana automatycznie - nie musisz nic więcej robić."
                : null,
            $"{siteUrl}/dashboard",
            "Przejdź do panelu");

        _ = _email.SendAsync(user.Email, "Twoja integracja z CARIZO jest aktywna", html)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _logger.LogError(t.Exception, "[PartnerSignupService] Wysyłka e-maila potwierdzającego nie powiodła się dla {Email}", user.Email);
            }, TaskContinuationOptions.OnlyOnFaulted);

        return Task.CompletedTask;
    }

    private static PartnerSignupRequestListDto MapToListDto(PartnerSignupRequest r) => new()
    {
        Id = r.Id,
        CompanyName = r.CompanyName,
        Email = r.Email,
        Phone = r.Phone,
        WebsiteUrl = r.WebsiteUrl,
        FeedUrl = r.FeedUrl,
        Format = r.FeedFormat?.ToString(),
        DetectedItemCount = r.DetectedItemCount,
        Status = r.Status.ToString(),
        CreatedAt = r.CreatedAt,
        ReviewedAt = r.ReviewedAt,
        RejectionReason = r.RejectionReason,
        PartnerId = r.PartnerId,
    };
}
