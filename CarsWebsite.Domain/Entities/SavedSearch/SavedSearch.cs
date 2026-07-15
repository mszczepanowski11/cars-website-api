namespace CarsWebsite;

public class SavedSearch
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    // Serialized SearchCarAdvertDto - stored as a string (rather than a normalized set of filter
    // columns) since the set of searchable fields is large and changes as the search page grows;
    // SavedSearchAlertJob deserializes it back to re-run the exact same search.
    public string CriteriaJson { get; set; } = "{}";

    public bool NotifyOnNew { get; set; } = true;
    public int NewResultsCount { get; set; } = 0;

    // Watermark for SavedSearchAlertJob: only adverts created after this are "new" for alerting
    // purposes. Starts at CreatedAt and advances every time the job checks this search, so the
    // same adverts are never counted twice across runs.
    public DateTime LastCheckedAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
