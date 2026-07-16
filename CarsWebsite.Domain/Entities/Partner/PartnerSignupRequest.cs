using System.ComponentModel.DataAnnotations;

namespace CarsWebsite;

public enum PartnerSignupStatus
{
    Pending,
    Approved,
    Rejected,
}

// A company self-registers via the public "Dla firm" form (POST /api/partner-signup) instead of
// an admin creating a Partner directly. Nothing here is live - approving one (AdminPartnerController)
// is what actually creates the Business User + Partner + runs the first import.
public class PartnerSignupRequest
{
    public int Id { get; set; }
    [MaxLength(200)] public string CompanyName { get; set; } = string.Empty;
    [MaxLength(200)] public string Email { get; set; } = string.Empty;
    [MaxLength(30)] public string Phone { get; set; } = string.Empty;
    [MaxLength(300)] public string? WebsiteUrl { get; set; }
    [MaxLength(500)] public string? FeedUrl { get; set; }
    public PartnerFeedFormat? FeedFormat { get; set; }

    // Snapshot from the last successful validation fetch, shown to the admin when reviewing -
    // the feed is re-fetched fresh at approval time regardless, this is just informational.
    public int? DetectedItemCount { get; set; }

    public PartnerSignupStatus Status { get; set; } = PartnerSignupStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public int? ReviewedByAdminId { get; set; }
    [MaxLength(500)] public string? RejectionReason { get; set; }

    public int? PartnerId { get; set; }
    public Partner? Partner { get; set; }
}
