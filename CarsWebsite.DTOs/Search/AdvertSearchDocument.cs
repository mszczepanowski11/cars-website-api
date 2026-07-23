using System.Text.Json.Serialization;

namespace cars_website_api.CarsWebsite.DTOs.Search;

// Meilisearch document for one CarAdvert. Deliberately NOT a full mirror of every searchable/
// filterable field on CarAdvert (see docs/search-engine-evaluation.md's sketched plan) - today this
// only powers the free-text Title/Description match that used to hit MySQL FULLTEXT directly
// (AdvertService.SearchCarAdvertsAsync still applies every structured filter - brand, price, year,
// EAV attributes, etc. - against MySQL exactly as before). The extra facet-shaped fields
// (BrandId/CategoryId/Price/Year) cost nothing to store now and mean a later pass that wants to move
// structured filtering into Meilisearch too doesn't need an index schema migration first.
public class AdvertSearchDocument
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("categoryId")]
    public int? CategoryId { get; set; }

    [JsonPropertyName("brandId")]
    public int? BrandId { get; set; }

    [JsonPropertyName("modelId")]
    public int? ModelId { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}
