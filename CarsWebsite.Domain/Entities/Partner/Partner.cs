namespace CarsWebsite;

// A company that submits its own inventory to CARIZO via the Partner API (POST /api/partner/adverts/import),
// authenticated with ApiKeyHash rather than the normal JWT-bearer login flow - partners integrate
// their own dealer-management software against this, not a human logging in through the site.
public class Partner
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;

    // BCrypt hash of the API key - the plaintext key is shown to the admin exactly once at
    // creation/regeneration time and never stored or displayed again, same handling as a password.
    public string ApiKeyHash { get; set; } = string.Empty;

    // Imported adverts are owned by this account (must be a Business account) - lets a partner's
    // listings show up in "Moje ogłoszenia"/dashboard exactly like any manually-created advert.
    public int LinkedUserId { get; set; }
    public User LinkedUser { get; set; } = null!;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastImportAt { get; set; }
}
