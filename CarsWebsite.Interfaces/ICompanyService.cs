using cars_website_api.CarsWebsite.DTOs.Company;

namespace cars_website_api.CarsWebsite.Interfaces;

public interface ICompanyService
{
    // The userId whose adverts the caller should act on: the caller's own id, unless the caller
    // is an Active Member of another account's company, in which case that Owner's id.
    Task<int> GetEffectiveOwnerIdAsync(int callerId);

    Task<MyCompanyContextDto> GetMyContextAsync(int userId);
    Task<IReadOnlyList<CompanyMemberDto>> GetMembersAsync(int ownerId);
    Task InviteMemberAsync(int ownerId, string email);
    Task CancelInviteAsync(int ownerId, int membershipId);
    Task RemoveMemberAsync(int ownerId, int membershipId);
    Task AcceptInviteAsync(string token, int acceptingUserId);
    Task LeaveCompanyAsync(int memberId);
}
