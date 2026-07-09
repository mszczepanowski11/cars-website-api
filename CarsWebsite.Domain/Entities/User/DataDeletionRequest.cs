using System;

namespace CarsWebsite
{
    // Log of Meta Data Deletion Callback requests (Meta Platform Terms 3(d)(i)) - proof, in case
    // of an audit, that a deletion request was received and actually acted on, not just accepted
    // and ignored. FacebookUserId is kept even after the User row's own FacebookId is cleared,
    // since it's the only way to answer Meta's status-check URL for this confirmation code later.
    public class DataDeletionRequest
    {
        public int Id { get; set; }
        public string FacebookUserId { get; set; } = string.Empty;
        public int? UserId { get; set; }
        public string ConfirmationCode { get; set; } = string.Empty;
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
    }
}
