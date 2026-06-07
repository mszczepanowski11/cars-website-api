using cars_website_api.CarsWebsite.Domain.Entities;

namespace CarsWebsite
{
    public enum ReportReason
    {
        Fraud,
        FalseData,
        InvalidVin,
        DuplicateAdvert,
        InappropriateContent,
        Spam,
        Other
    }

    public enum ReportTargetType
    {
        Advert,
        User
    }

    public enum ReportStatus
    {
        Pending,
        Resolved,
        Rejected
    }

    public class Report
    {
        public int Id { get; set; }
        public ReportTargetType TargetType { get; set; }
        public int? TargetAdvertId { get; set; }
        public Advert? TargetAdvert { get; set; }
        public int? TargetUserId { get; set; }
        public User? TargetUser { get; set; }
        public ReportReason Reason { get; set; }
        public string? Content { get; set; }
        public DateTime ReportedAt { get; set; } = DateTime.UtcNow;
        public int ReportedByUserId { get; set; }
        public User ReportedBy { get; set; } = null!;
        public ReportStatus Status { get; set; } = ReportStatus.Pending;
        public DateTime? ResolvedAt { get; set; }
        public int? ResolvedByAdminId { get; set; }
        public string? AdminNote { get; set; }
    }
}