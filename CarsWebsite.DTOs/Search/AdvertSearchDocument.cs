using System.Text.Json.Serialization;

namespace cars_website_api.CarsWebsite.DTOs.Search;

// Meilisearch document for one CarAdvert. Powers both the free-text Title/Description match
// (that used to hit MySQL FULLTEXT directly) and, since the Etap 4 attribute-filter pass, the
// structured facet filters too - brand/model/category/price/year plus every EAV attribute value
// (AdvertAttributeValue), so AdvertService.SearchCarAdvertsAsync can route a search with
// AttributeFilters through Meilisearch instead of the per-filter EF `EXISTS` subqueries against
// MySQL, falling back to MySQL exactly as before whenever Meilisearch is disabled/unreachable.
//
// Attributes is a JsonExtensionData dictionary so each EAV attribute value serializes as a
// top-level field (e.g. "attr_5": true) rather than a nested object - Meilisearch's filter
// language only supports plain top-level (or dot-path) field names, and a flat "attr_{id}" key
// per AttributeDefinition.Id is the simplest thing that works without a schema migration every
// time an admin adds a new attribute definition (see MeilisearchAdvertIndexService.ReindexAllAsync,
// which derives the full filterableAttributes list from AttributeDefinition at reindex time).
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

    [JsonExtensionData]
    public Dictionary<string, object>? Attributes { get; set; }
}
