namespace CarsWebsite;

public enum CompanyMembershipStatus
{
    Pending = 0,
    Active = 1,
    Removed = 2,
}

// Owner+Member model (CTO audit Etap 3, "granularne role dla kont firmowych"): the Owner is just
// the existing Business-type User row, not a membership row of its own - this table only ever
// represents Members invited onto that Owner's account. A Member acts on the Owner's adverts (see
// ICompanyService.GetEffectiveOwnerIdAsync) but never gets access to the Owner's payments,
// subscription, or account settings - those stay keyed strictly to the Owner's own userId.
public class CompanyMembership
{
    public int Id { get; set; }

    public int OwnerId { get; set; }
    public User Owner { get; set; } = null!;

    // Null until a matching account accepts the invite.
    public int? MemberId { get; set; }
    public User? Member { get; set; }

    public string InvitedEmail { get; set; } = string.Empty;
    public string InviteToken { get; set; } = string.Empty;
    public DateTime InviteTokenExpires { get; set; }

    public CompanyMembershipStatus Status { get; set; } = CompanyMembershipStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAt { get; set; }
    public DateTime? RemovedAt { get; set; }
}
