using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs.Partner;

public class PartnerResponseDto
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public int LinkedUserId { get; set; }
    public string LinkedUserEmail { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastImportAt { get; set; }

    // CTO audit Etap 2: previously only settable via the "Dla firm" self-service signup approval
    // flow - an admin creating/editing a partner directly had no way to see or configure these.
    public string? FeedUrl { get; set; }
    public string? FeedFormat { get; set; }
    public bool AutoSyncEnabled { get; set; }

    // Sync health (CTO audit Etap 2) - tracked only by the scheduled PartnerFeedSyncJob run, not
    // by a manual "sync now" (see Partner.cs for why).
    public int ConsecutiveSyncFailures { get; set; }
    public string? LastSyncError { get; set; }
    public DateTime? LastSyncAttemptAt { get; set; }
}

public class CreatePartnerDto
{
    [Required] [MaxLength(200)] public string CompanyName { get; set; } = string.Empty;
    [Required] [EmailAddress] [MaxLength(200)] public string ContactEmail { get; set; } = string.Empty;

    [Required] public int LinkedUserId { get; set; }

    [MaxLength(500)] public string? FeedUrl { get; set; }
    public global::CarsWebsite.PartnerFeedFormat? FeedFormat { get; set; }
    public bool AutoSyncEnabled { get; set; } = true;
}

public class UpdatePartnerDto
{
    [Required] [MaxLength(200)] public string CompanyName { get; set; } = string.Empty;
    [Required] [EmailAddress] [MaxLength(200)] public string ContactEmail { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    [MaxLength(500)] public string? FeedUrl { get; set; }
    public global::CarsWebsite.PartnerFeedFormat? FeedFormat { get; set; }
    public bool AutoSyncEnabled { get; set; } = true;
}

// Returned only once, right after creation or key regeneration - the plaintext key is never
// stored or retrievable again afterward (same handling as a user password).
public class PartnerApiKeyResponseDto
{
    public int PartnerId { get; set; }
    public string ApiKey { get; set; } = string.Empty;
}

public class PartnerFieldMappingDto
{
    [Required] public string OurField { get; set; } = string.Empty;
    [Required] [MaxLength(200)] public string SourcePath { get; set; } = string.Empty;
}

public class PartnerValueMappingDto
{
    [Required] public string Field { get; set; } = string.Empty;
    [Required] [MaxLength(200)] public string ExternalValue { get; set; } = string.Empty;
    [Required] [MaxLength(200)] public string InternalValue { get; set; } = string.Empty;
}

public class PartnerImportLogResponseDto
{
    public int Id { get; set; }
    public int PartnerId { get; set; }
    public string Format { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int ItemsTotal { get; set; }
    public int ItemsCreated { get; set; }
    public int ItemsUpdated { get; set; }
    public int ItemsFailed { get; set; }
    public string? ErrorSummary { get; set; }
}
