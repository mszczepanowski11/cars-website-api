using System;

namespace CarsWebsite
{
    // Audit trail for explicit consent given before an account gets auto-created from a
    // third-party login provider (Facebook, later Google/Instagram) - kept separate from the
    // login event itself so consent is provably recorded (RODO art. 5 ust. 2 - rozliczalność),
    // not just implied by the fact that login succeeded.
    public class ConsentRecord
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string ConsentType { get; set; } = string.Empty;
        public string PolicyVersion { get; set; } = string.Empty;
        public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
    }
}
