namespace CarsWebsite
{
    public enum AdminActionType
    {
        HideAdvert,
        UnhideAdvert,
        DeleteAdvert,
        BlockUser,
        UnblockUser,
        ResolveReport,
        RejectReport,
        EditAdvert,
        ActivateAdvert,
        DeactivateAdvert,
        DeleteUser,
        CreateAdvertForClient,
        ResendClientActivationEmail,
        ActivateUser,
        ApprovePartnerSignup,
        RejectPartnerSignup
    }

    public class AdminActionLog
    {
        public int Id { get; set; }
        public int AdminUserId { get; set; }
        public User Admin { get; set; } = null!;
        public AdminActionType ActionType { get; set; }
        public int? TargetAdvertId { get; set; }
        public int? TargetUserId { get; set; }
        public int? ReportId { get; set; }
        public string? Note { get; set; }
        public DateTime PerformedAt { get; set; } = DateTime.UtcNow;
    }
}