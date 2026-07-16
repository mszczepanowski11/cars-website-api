namespace cars_website_api.CarsWebsite.Interfaces;

public class PartnerFeedFetchResult
{
    public bool Success { get; set; }
    public string? Content { get; set; }
    public CarsWebsite.PartnerFeedFormat Format { get; set; }
    public string? Error { get; set; }
}

// Fetches a partner-supplied feed URL - this is a public, unauthenticated write surface (the
// "Dla firm" signup form) making the server issue an outbound HTTP request to an address the
// caller controls, so the implementation MUST guard against SSRF (private/loopback/link-local
// targets, redirects, oversized/slow responses) rather than just wrapping HttpClient.GetAsync.
public interface IPartnerFeedFetchService
{
    Task<PartnerFeedFetchResult> FetchAsync(string url);
}
