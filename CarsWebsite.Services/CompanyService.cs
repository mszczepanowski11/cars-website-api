using System.Security.Cryptography;
using cars_website_api.CarsWebsite.DTOs.Company;
using cars_website_api.CarsWebsite.Interfaces;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace cars_website_api.CarsWebsite.Services;

public class CompanyService : ICompanyService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IEmailService _email;
    private readonly ILogger<CompanyService> _logger;

    public CompanyService(AppDbContext context, IConfiguration configuration, IEmailService email, ILogger<CompanyService> logger)
    {
        _context = context;
        _configuration = configuration;
        _email = email;
        _logger = logger;
    }

    public async Task<int> GetEffectiveOwnerIdAsync(int callerId)
    {
        var ownerId = await _context.CompanyMemberships
            .Where(cm => cm.MemberId == callerId && cm.Status == CompanyMembershipStatus.Active)
            .Select(cm => (int?)cm.OwnerId)
            .FirstOrDefaultAsync();
        return ownerId ?? callerId;
    }

    public async Task<MyCompanyContextDto> GetMyContextAsync(int userId)
    {
        var membership = await _context.CompanyMemberships
            .AsNoTracking()
            .Where(cm => cm.MemberId == userId && cm.Status == CompanyMembershipStatus.Active)
            .Select(cm => new { cm.OwnerId, cm.Owner.CompanyName, cm.Owner.Name, cm.Owner.Surname })
            .FirstOrDefaultAsync();

        if (membership != null)
        {
            return new MyCompanyContextDto
            {
                IsMember = true,
                IsOwner = false,
                OwnerId = membership.OwnerId,
                OwnerCompanyName = membership.CompanyName ?? $"{membership.Name} {membership.Surname}",
            };
        }

        var hasTeam = await _context.CompanyMemberships
            .AsNoTracking()
            .AnyAsync(cm => cm.OwnerId == userId && cm.Status != CompanyMembershipStatus.Removed);

        return new MyCompanyContextDto { IsOwner = hasTeam, IsMember = false };
    }

    public async Task<IReadOnlyList<CompanyMemberDto>> GetMembersAsync(int ownerId)
    {
        return await _context.CompanyMemberships
            .AsNoTracking()
            .Where(cm => cm.OwnerId == ownerId && cm.Status != CompanyMembershipStatus.Removed)
            .OrderByDescending(cm => cm.CreatedAt)
            .Select(cm => new CompanyMemberDto
            {
                MembershipId = cm.Id,
                MemberId = cm.MemberId,
                Email = cm.Member != null ? cm.Member.Email : cm.InvitedEmail,
                Name = cm.Member != null ? cm.Member.Name : null,
                Surname = cm.Member != null ? cm.Member.Surname : null,
                Status = cm.Status,
                CreatedAt = cm.CreatedAt,
                AcceptedAt = cm.AcceptedAt,
            })
            .ToListAsync();
    }

    public async Task InviteMemberAsync(int ownerId, string email)
    {
        var normalizedEmail = (email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
            throw new ArgumentException("Podaj adres email.");

        var owner = await _context.Users.FirstOrDefaultAsync(u => u.Id == ownerId)
            ?? throw new KeyNotFoundException("Użytkownik nie istnieje.");

        if (owner.AccountType != AccountType.Business)
            throw new InvalidOperationException("Tylko konta biznesowe mogą zapraszać członków zespołu.");

        if (normalizedEmail == owner.Email)
            throw new InvalidOperationException("Nie możesz zaprosić samego siebie.");

        // The Owner role only exists as "the Business account itself" - inviting someone who is
        // already an Owner of their own team would make GetEffectiveOwnerIdAsync ambiguous about
        // whose adverts they're editing, so it's disallowed rather than silently picking one.
        var invitedUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        if (invitedUser != null)
        {
            var invitedOwnsTeam = await _context.CompanyMemberships
                .AnyAsync(cm => cm.OwnerId == invitedUser.Id && cm.Status != CompanyMembershipStatus.Removed);
            if (invitedOwnsTeam)
                throw new InvalidOperationException("Ten użytkownik zarządza już własnym zespołem i nie może dołączyć do innego.");

            var alreadyActiveElsewhere = await _context.CompanyMemberships
                .AnyAsync(cm => cm.MemberId == invitedUser.Id && cm.Status == CompanyMembershipStatus.Active);
            if (alreadyActiveElsewhere)
                throw new InvalidOperationException("Ten użytkownik jest już członkiem innego zespołu.");
        }

        var existingPending = await _context.CompanyMemberships
            .FirstOrDefaultAsync(cm => cm.OwnerId == ownerId && cm.InvitedEmail == normalizedEmail
                && cm.Status == CompanyMembershipStatus.Pending);

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var expires = DateTime.UtcNow.AddDays(7);

        if (existingPending != null)
        {
            existingPending.InviteToken = token;
            existingPending.InviteTokenExpires = expires;
            existingPending.CreatedAt = DateTime.UtcNow;
        }
        else
        {
            var alreadyActiveMember = await _context.CompanyMemberships
                .AnyAsync(cm => cm.OwnerId == ownerId && cm.InvitedEmail == normalizedEmail
                    && cm.Status == CompanyMembershipStatus.Active);
            if (alreadyActiveMember)
                throw new InvalidOperationException("Ten użytkownik jest już członkiem Twojego zespołu.");

            _context.CompanyMemberships.Add(new CompanyMembership
            {
                OwnerId = ownerId,
                InvitedEmail = normalizedEmail,
                InviteToken = token,
                InviteTokenExpires = expires,
                Status = CompanyMembershipStatus.Pending,
            });
        }

        await _context.SaveChangesAsync();

        var siteUrl = _configuration["SiteUrl"] ?? "https://carizo.eu";
        var companyLabel = owner.CompanyName ?? $"{owner.Name} {owner.Surname}";
        var subject = $"Zaproszenie do zespołu {companyLabel} – CARIZO";
        var html = EmailService.BuildHtml(
            "Zaproszenie do zespołu",
            $"Użytkownik {companyLabel} zaprosił Cię do współzarządzania ogłoszeniami firmowymi na CARIZO. Link jest ważny przez 7 dni.",
            null,
            $"{siteUrl}/zespol/zaproszenie?token={token}",
            "Dołącz do zespołu");

        _ = _email.SendAsync(normalizedEmail, subject, html)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _logger.LogError(t.Exception, "[Company/Invite] Email wysyłki nie powiódł się dla {Email}", normalizedEmail);
            }, TaskContinuationOptions.OnlyOnFaulted);

        _logger.LogInformation("[Company/Invite] ownerId={OwnerId} invitedEmail={Email}", ownerId, normalizedEmail);
    }

    public async Task CancelInviteAsync(int ownerId, int membershipId)
    {
        var membership = await _context.CompanyMemberships
            .FirstOrDefaultAsync(cm => cm.Id == membershipId && cm.OwnerId == ownerId)
            ?? throw new KeyNotFoundException("Zaproszenie nie istnieje.");

        if (membership.Status != CompanyMembershipStatus.Pending)
            throw new InvalidOperationException("Można anulować tylko oczekujące zaproszenia.");

        _context.CompanyMemberships.Remove(membership);
        await _context.SaveChangesAsync();
    }

    public async Task RemoveMemberAsync(int ownerId, int membershipId)
    {
        var membership = await _context.CompanyMemberships
            .FirstOrDefaultAsync(cm => cm.Id == membershipId && cm.OwnerId == ownerId)
            ?? throw new KeyNotFoundException("Członkostwo nie istnieje.");

        if (membership.Status != CompanyMembershipStatus.Active)
            throw new InvalidOperationException("Ten użytkownik nie jest aktywnym członkiem zespołu.");

        membership.Status = CompanyMembershipStatus.Removed;
        membership.RemovedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("[Company/Remove] ownerId={OwnerId} membershipId={MembershipId} memberId={MemberId}",
            ownerId, membershipId, membership.MemberId);
    }

    public async Task AcceptInviteAsync(string token, int acceptingUserId)
    {
        var membership = await _context.CompanyMemberships
            .Include(cm => cm.Owner)
            .FirstOrDefaultAsync(cm => cm.InviteToken == token && cm.Status == CompanyMembershipStatus.Pending)
            ?? throw new InvalidOperationException("Zaproszenie jest nieprawidłowe lub zostało już wykorzystane.");

        if (membership.InviteTokenExpires < DateTime.UtcNow)
            throw new InvalidOperationException("Zaproszenie wygasło. Poproś o nowe.");

        var acceptingUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == acceptingUserId)
            ?? throw new KeyNotFoundException("Użytkownik nie istnieje.");

        if (acceptingUser.Email != membership.InvitedEmail)
            throw new InvalidOperationException("To zaproszenie zostało wysłane na inny adres email. Zaloguj się na konto z zaproszonym adresem.");

        var ownsTeam = await _context.CompanyMemberships
            .AnyAsync(cm => cm.OwnerId == acceptingUserId && cm.Status != CompanyMembershipStatus.Removed);
        if (ownsTeam)
            throw new InvalidOperationException("Zarządzasz już własnym zespołem i nie możesz dołączyć do innego.");

        var alreadyActiveElsewhere = await _context.CompanyMemberships
            .AnyAsync(cm => cm.MemberId == acceptingUserId && cm.Status == CompanyMembershipStatus.Active);
        if (alreadyActiveElsewhere)
            throw new InvalidOperationException("Jesteś już członkiem innego zespołu. Opuść go, aby dołączyć do nowego.");

        membership.MemberId = acceptingUserId;
        membership.Status = CompanyMembershipStatus.Active;
        membership.AcceptedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("[Company/Accept] ownerId={OwnerId} memberId={MemberId}", membership.OwnerId, acceptingUserId);
    }

    public async Task LeaveCompanyAsync(int memberId)
    {
        var membership = await _context.CompanyMemberships
            .FirstOrDefaultAsync(cm => cm.MemberId == memberId && cm.Status == CompanyMembershipStatus.Active)
            ?? throw new InvalidOperationException("Nie jesteś członkiem żadnego zespołu.");

        membership.Status = CompanyMembershipStatus.Removed;
        membership.RemovedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }
}
