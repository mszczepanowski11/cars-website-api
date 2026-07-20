using System.ComponentModel.DataAnnotations;

namespace CarsWebsite;

// First concrete brick of the "Business Directory" from the platform blueprint (section 17):
// a public, searchable catalogue of automotive/transport companies, independent of Partner/User.
// A Partner (a company that pushes feeds) MAY later link to a DirectoryCompany, but the directory
// exists on its own - most of its rows come from seeds/imports, not from registered accounts.
//
// Every row carries a stable global identifier (PublicId, the "Carizo ID" from the blueprint,
// e.g. "crz:org:pl:a3f9c2..."). The rest of the graph is meant to reference companies by this id,
// never by the internal auto-increment Id, so the identifier survives re-imports and migrations.
public class DirectoryCompany
{
    public int Id { get; set; }

    // Global Carizo ID - crz:org:<country>:<token>. Assigned once at creation, never reused.
    [MaxLength(64)] public string PublicId { get; set; } = string.Empty;

    // URL slug for /firmy/{slug} - unique, derived from name (+ city on collision).
    [MaxLength(220)] public string Slug { get; set; } = string.Empty;

    [MaxLength(200)] public string Name { get; set; } = string.Empty;
    // Normalized (lowercased, accent-stripped) name for idempotent seeding / dedup matching.
    [MaxLength(200)] public string NameNormalized { get; set; } = string.Empty;

    // Directory category slug (komisy, dealerzy, warsztaty, transport, ...). Free-form string
    // rather than an FK so the directory taxonomy can grow independently of VehicleCategory.
    [MaxLength(60)] public string Category { get; set; } = string.Empty;

    [MaxLength(2)]   public string? CountryCode { get; set; }   // ISO 3166-1 alpha-2, e.g. "PL"
    [MaxLength(120)] public string? City { get; set; }
    [MaxLength(250)] public string? Address { get; set; }
    [MaxLength(20)]  public string? PostalCode { get; set; }
    [MaxLength(40)]  public string? Phone { get; set; }         // E.164 where possible
    [MaxLength(200)] public string? Email { get; set; }
    // 'role' (biuro@/kontakt@), 'personal', or 'unknown' - drives compliance handling later.
    [MaxLength(20)]  public string? EmailType { get; set; }
    [MaxLength(300)] public string? Website { get; set; }
    [MaxLength(300)] public string? ProfileUrl { get; set; }
    [MaxLength(5)]   public string? Language { get; set; }      // ISO 639-1, e.g. "pl"

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // active | unverified | closed. Seeded rows start 'unverified' until confirmed.
    [MaxLength(20)] public string Status { get; set; } = "unverified";

    // Provenance - which pipeline produced this row (seed:leads, dla-firm, osm, ...).
    [MaxLength(60)] public string? Source { get; set; }

    // If a registered Partner claimed / matches this company.
    public int? PartnerId { get; set; }
    public Partner? Partner { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
