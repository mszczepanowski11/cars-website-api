using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.DTOs.Advert;
using cars_website_api.CarsWebsite.Interfaces;
using cars_website_api.CarsWebsite.Services;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CarsWebsiteTests;

// Fire-and-forget sink - CompanyService.InviteMemberAsync sends the invite email without
// awaiting it, so tests only need a working interface implementation.
public class NullEmailService : IEmailService
{
    public Task SendAsync(string to, string subject, string htmlBody) => Task.CompletedTask;
}

// Owner+Member multi-user company accounts (CTO audit Etap 3, "granularne role dla kont
// firmowych"). Covers the invite/accept/remove lifecycle and the effective-owner resolution that
// AdvertController relies on to let a Member manage the Owner's adverts.
public class CompanyServiceTests
{
    private static (AppDbContext Context, ICompanyService Service) Setup()
    {
        var context = TestDbContextFactory.CreateContext(Guid.NewGuid().ToString());
        var config = new ConfigurationBuilder().Build();
        var service = new CompanyService(context, config, new NullEmailService(), NullLogger<CompanyService>.Instance);
        return (context, service);
    }

    private static async Task<User> CreateUserAsync(AppDbContext context, string email, AccountType accountType)
    {
        var user = new User
        {
            // Every real registration path normalizes to lowercase (see AuthService.Register) and
            // CompanyService.InviteMemberAsync's email lookups rely on that invariant - mirror it
            // here so the test data matches production reality.
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = "test-hash-not-used",
            Name = "Test",
            Surname = "User",
            PhoneNumber = "+48500000000",
            AccountType = accountType,
            EmailVerified = true,
            CreatedAt = DateTime.UtcNow,
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static async Task<string> InviteAndGetTokenAsync(AppDbContext context, ICompanyService service, int ownerId, string email)
    {
        await service.InviteMemberAsync(ownerId, email);
        return await context.CompanyMemberships
            .Where(cm => cm.OwnerId == ownerId && cm.InvitedEmail == email.ToLowerInvariant())
            .Select(cm => cm.InviteToken)
            .FirstAsync();
    }

    [Fact]
    public async Task Invite_ByBusinessOwner_CreatesPendingMembership()
    {
        var (context, service) = Setup();
        var owner = await CreateUserAsync(context, "owner@company.test", AccountType.Business);

        await service.InviteMemberAsync(owner.Id, "Employee@Company.test");

        var membership = await context.CompanyMemberships.FirstAsync(cm => cm.OwnerId == owner.Id);
        Assert.Equal(CompanyMembershipStatus.Pending, membership.Status);
        Assert.Equal("employee@company.test", membership.InvitedEmail);
        Assert.Null(membership.MemberId);
    }

    [Fact]
    public async Task Invite_ByPersonalAccount_Throws()
    {
        var (context, service) = Setup();
        var owner = await CreateUserAsync(context, "personal@company.test", AccountType.Personal);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InviteMemberAsync(owner.Id, "employee@company.test"));
    }

    [Fact]
    public async Task Invite_Self_Throws()
    {
        var (context, service) = Setup();
        var owner = await CreateUserAsync(context, "owner@company.test", AccountType.Business);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InviteMemberAsync(owner.Id, "owner@company.test"));
    }

    [Fact]
    public async Task Invite_UserWhoOwnsOwnTeam_Throws()
    {
        var (context, service) = Setup();
        var ownerA = await CreateUserAsync(context, "ownerA@company.test", AccountType.Business);
        var ownerB = await CreateUserAsync(context, "ownerB@company.test", AccountType.Business);
        // ownerB already runs their own team (has at least one non-removed membership row).
        await service.InviteMemberAsync(ownerB.Id, "someone@company.test");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InviteMemberAsync(ownerA.Id, "ownerB@company.test"));
    }

    [Fact]
    public async Task AcceptInvite_WithMatchingEmail_ActivatesMembership()
    {
        var (context, service) = Setup();
        var owner = await CreateUserAsync(context, "owner@company.test", AccountType.Business);
        var employee = await CreateUserAsync(context, "employee@company.test", AccountType.Personal);
        var token = await InviteAndGetTokenAsync(context, service, owner.Id, employee.Email);

        await service.AcceptInviteAsync(token, employee.Id);

        var membership = await context.CompanyMemberships.FirstAsync(cm => cm.OwnerId == owner.Id);
        Assert.Equal(CompanyMembershipStatus.Active, membership.Status);
        Assert.Equal(employee.Id, membership.MemberId);
        Assert.NotNull(membership.AcceptedAt);
    }

    [Fact]
    public async Task AcceptInvite_WithWrongUser_Throws()
    {
        var (context, service) = Setup();
        var owner = await CreateUserAsync(context, "owner@company.test", AccountType.Business);
        var employee = await CreateUserAsync(context, "employee@company.test", AccountType.Personal);
        var stranger = await CreateUserAsync(context, "stranger@company.test", AccountType.Personal);
        var token = await InviteAndGetTokenAsync(context, service, owner.Id, employee.Email);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AcceptInviteAsync(token, stranger.Id));
    }

    [Fact]
    public async Task AcceptInvite_Expired_Throws()
    {
        var (context, service) = Setup();
        var owner = await CreateUserAsync(context, "owner@company.test", AccountType.Business);
        var employee = await CreateUserAsync(context, "employee@company.test", AccountType.Personal);
        var token = await InviteAndGetTokenAsync(context, service, owner.Id, employee.Email);

        var membership = await context.CompanyMemberships.FirstAsync(cm => cm.InviteToken == token);
        membership.InviteTokenExpires = DateTime.UtcNow.AddDays(-1);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AcceptInviteAsync(token, employee.Id));
    }

    [Fact]
    public async Task InviteMemberAsync_EmailAlreadyActiveElsewhere_Throws()
    {
        var (context, service) = Setup();
        var ownerA = await CreateUserAsync(context, "ownerA@company.test", AccountType.Business);
        var ownerB = await CreateUserAsync(context, "ownerB@company.test", AccountType.Business);
        var employee = await CreateUserAsync(context, "employee@company.test", AccountType.Personal);

        var tokenA = await InviteAndGetTokenAsync(context, service, ownerA.Id, employee.Email);
        await service.AcceptInviteAsync(tokenA, employee.Id);

        // Fails fast at invite time, not just at accept time - the two owners shouldn't even be
        // able to send a competing invite to an already-committed employee.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InviteMemberAsync(ownerB.Id, employee.Email));
    }

    // AcceptInviteAsync carries its own "already active elsewhere" guard independent of
    // InviteMemberAsync's - exercised directly here by inserting a second Pending row straight
    // into the DB (bypassing InviteMemberAsync's own earlier check) so a stale/pre-existing
    // invite can't be used to bypass the one-company-at-a-time rule after the fact.
    [Fact]
    public async Task AcceptInvite_AlreadyMemberOfAnotherCompany_Throws()
    {
        var (context, service) = Setup();
        var ownerA = await CreateUserAsync(context, "ownerA@company.test", AccountType.Business);
        var ownerB = await CreateUserAsync(context, "ownerB@company.test", AccountType.Business);
        var employee = await CreateUserAsync(context, "employee@company.test", AccountType.Personal);

        var tokenA = await InviteAndGetTokenAsync(context, service, ownerA.Id, employee.Email);
        await service.AcceptInviteAsync(tokenA, employee.Id);

        const string staleToken = "stale-pending-token-from-before-employee-joined-ownera";
        context.CompanyMemberships.Add(new CompanyMembership
        {
            OwnerId = ownerB.Id,
            InvitedEmail = employee.Email,
            InviteToken = staleToken,
            InviteTokenExpires = DateTime.UtcNow.AddDays(7),
            Status = CompanyMembershipStatus.Pending,
        });
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AcceptInviteAsync(staleToken, employee.Id));
    }

    [Fact]
    public async Task GetEffectiveOwnerIdAsync_ForActiveMember_ReturnsOwnerId()
    {
        var (context, service) = Setup();
        var owner = await CreateUserAsync(context, "owner@company.test", AccountType.Business);
        var employee = await CreateUserAsync(context, "employee@company.test", AccountType.Personal);
        var token = await InviteAndGetTokenAsync(context, service, owner.Id, employee.Email);
        await service.AcceptInviteAsync(token, employee.Id);

        var effectiveOwnerId = await service.GetEffectiveOwnerIdAsync(employee.Id);

        Assert.Equal(owner.Id, effectiveOwnerId);
    }

    [Fact]
    public async Task GetEffectiveOwnerIdAsync_ForNonMember_ReturnsSelf()
    {
        var (context, service) = Setup();
        var user = await CreateUserAsync(context, "solo@company.test", AccountType.Personal);

        var effectiveOwnerId = await service.GetEffectiveOwnerIdAsync(user.Id);

        Assert.Equal(user.Id, effectiveOwnerId);
    }

    [Fact]
    public async Task RemoveMember_RevertsEffectiveOwnerToSelf()
    {
        var (context, service) = Setup();
        var owner = await CreateUserAsync(context, "owner@company.test", AccountType.Business);
        var employee = await CreateUserAsync(context, "employee@company.test", AccountType.Personal);
        var token = await InviteAndGetTokenAsync(context, service, owner.Id, employee.Email);
        await service.AcceptInviteAsync(token, employee.Id);
        var membership = await context.CompanyMemberships.FirstAsync(cm => cm.OwnerId == owner.Id);

        await service.RemoveMemberAsync(owner.Id, membership.Id);

        var effectiveOwnerId = await service.GetEffectiveOwnerIdAsync(employee.Id);
        Assert.Equal(employee.Id, effectiveOwnerId);
    }

    [Fact]
    public async Task LeaveCompany_DeactivatesMembership()
    {
        var (context, service) = Setup();
        var owner = await CreateUserAsync(context, "owner@company.test", AccountType.Business);
        var employee = await CreateUserAsync(context, "employee@company.test", AccountType.Personal);
        var token = await InviteAndGetTokenAsync(context, service, owner.Id, employee.Email);
        await service.AcceptInviteAsync(token, employee.Id);

        await service.LeaveCompanyAsync(employee.Id);

        var effectiveOwnerId = await service.GetEffectiveOwnerIdAsync(employee.Id);
        Assert.Equal(employee.Id, effectiveOwnerId);
    }

    [Fact]
    public async Task CancelInvite_OnActiveMembership_Throws()
    {
        var (context, service) = Setup();
        var owner = await CreateUserAsync(context, "owner@company.test", AccountType.Business);
        var employee = await CreateUserAsync(context, "employee@company.test", AccountType.Personal);
        var token = await InviteAndGetTokenAsync(context, service, owner.Id, employee.Email);
        await service.AcceptInviteAsync(token, employee.Id);
        var membership = await context.CompanyMemberships.FirstAsync(cm => cm.OwnerId == owner.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CancelInviteAsync(owner.Id, membership.Id));
    }

    // Exercises the exact pattern AdvertController.ResolveActingUserIdAsync + IAdvertService.Create
    // follow in production: resolve the effective owner first, then create the advert against that
    // id - proving a Member's advert ends up owned by the company (Owner), not by the Member.
    [Fact]
    public async Task MemberCreatingAdvert_ViaEffectiveOwnerId_BelongsToCompanyOwner()
    {
        var (context, service) = Setup();
        var owner = await CreateUserAsync(context, "owner@company.test", AccountType.Business);
        var employee = await CreateUserAsync(context, "employee@company.test", AccountType.Personal);
        var token = await InviteAndGetTokenAsync(context, service, owner.Id, employee.Email);
        await service.AcceptInviteAsync(token, employee.Id);

        var advertService = TestDbContextFactory.CreateAdvertService(context);

        var effectiveOwnerId = await service.GetEffectiveOwnerIdAsync(employee.Id);
        var advertId = await advertService.CreateCarAdvertAsync(new CreateCarAdvertDto
        {
            Title = "Test advert",
            Description = "desc",
            Price = 10000,
            Condition = "used",
            SellerType = "dealer",
        }, effectiveOwnerId);

        var advert = await context.CarAdverts.AsNoTracking().FirstAsync(a => a.Id == advertId);
        Assert.Equal(owner.Id, advert.UserId);
        Assert.NotEqual(employee.Id, advert.UserId);
    }
}
