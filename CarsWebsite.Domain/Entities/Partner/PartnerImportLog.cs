namespace CarsWebsite;

public enum PartnerFeedFormat
{
    Xml,
    Csv,
}

public class PartnerImportLog
{
    public int Id { get; set; }

    public int PartnerId { get; set; }
    public Partner Partner { get; set; } = null!;

    public PartnerFeedFormat Format { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public int ItemsTotal { get; set; }
    public int ItemsCreated { get; set; }
    public int ItemsUpdated { get; set; }
    public int ItemsFailed { get; set; }

    // Newline-joined per-item error messages (e.g. "row 12: unknown brand 'Fiatt'") - capped in
    // length by the import service so one badly malformed feed can't write an unbounded blob.
    public string? ErrorSummary { get; set; }
}
