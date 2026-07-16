using System.ComponentModel.DataAnnotations;

namespace cars_website_api.CarsWebsite.DTOs.Partner;

public class PartnerSignupInputDto
{
    [Required] [MaxLength(200)] public string CompanyName { get; set; } = string.Empty;
    [Required] [EmailAddress] [MaxLength(200)] public string Email { get; set; } = string.Empty;
    [Required] [MaxLength(30)] public string Phone { get; set; } = string.Empty;
    [MaxLength(300)] public string? WebsiteUrl { get; set; }
    [MaxLength(500)] public string? FeedUrl { get; set; }
}

public class PartnerSignupPreviewResultDto
{
    public bool Valid { get; set; }
    public int? ItemCount { get; set; }
    public string? Format { get; set; }
    public string? Error { get; set; }
    // True when no FeedUrl was supplied at all - a valid, expected case (company wants manual
    // onboarding), distinct from "supplied a URL that failed to validate".
    public bool NoFeedProvided { get; set; }
}

public class PartnerSignupResponseDto
{
    public int Id { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class PartnerSignupRequestListDto
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? WebsiteUrl { get; set; }
    public string? FeedUrl { get; set; }
    public string? Format { get; set; }
    public int? DetectedItemCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
    public int? PartnerId { get; set; }
}

public class RejectPartnerSignupDto
{
    [MaxLength(500)] public string? Reason { get; set; }
}

public class ApprovePartnerSignupResultDto
{
    public int PartnerId { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public bool WasNewAccount { get; set; }
    public int? ImportedItemsCreated { get; set; }
    public int? ImportedItemsFailed { get; set; }
}
