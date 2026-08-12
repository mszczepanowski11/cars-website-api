using CarsWebsite;

namespace cars_website_api.CarsWebsite.DTOs.Company;

public class CompanyMemberDto
{
    public int MembershipId { get; set; }
    public int? MemberId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Surname { get; set; }
    public CompanyMembershipStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
}

public class InviteMemberDto
{
    public string Email { get; set; } = string.Empty;
}

public class AcceptInviteDto
{
    public string Token { get; set; } = string.Empty;
}

// What the currently logged-in user should know about their own place in a company: whether
// they're acting as an Owner, a Member working for someone else's account, or neither.
public class MyCompanyContextDto
{
    public bool IsOwner { get; set; }
    public bool IsMember { get; set; }
    public int? OwnerId { get; set; }
    public string? OwnerCompanyName { get; set; }
}
