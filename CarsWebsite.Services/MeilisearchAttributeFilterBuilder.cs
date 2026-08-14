using System.Globalization;
using cars_website_api.CarsWebsite.DTOs.Advert;

namespace cars_website_api.CarsWebsite.Services;

// Translates SearchCarAdvertDto.AttributeFilters into a Meilisearch filter-expression string,
// mirroring exactly the semantics of the EF `EXISTS` subqueries in AdvertService.
// SearchCarAdvertsInternalAsync that this replaces when Meilisearch is enabled: each
// AttributeFilterDto is one AND'ed criterion, and its value slots map onto the same
// attr_{AttributeDefinitionId} fields MeilisearchAdvertIndexService indexes (see
// AdvertSearchDocument.Attributes).
public static class MeilisearchAttributeFilterBuilder
{
    // Returns null when there's nothing to filter on (empty/null list), matching the
    // `if (dto.AttributeFilters != null && dto.AttributeFilters.Any())` guard in AdvertService.
    public static string? Build(List<AttributeFilterDto>? filters)
    {
        if (filters == null || filters.Count == 0) return null;

        var clauses = new List<string>();
        foreach (var af in filters)
        {
            var field = $"attr_{af.AttributeDefinitionId}";
            if (af.ValueBool.HasValue)
            {
                clauses.Add($"{field} = {(af.ValueBool.Value ? "true" : "false")}");
            }
            else if (af.ValueTextIn != null && af.ValueTextIn.Count > 0)
            {
                // CONTAINS (not `=`) to match the EF fallback's `ValueText.Contains(val)` substring
                // semantics exactly - a MultiSelect attribute stores every chosen option in one
                // delimited ValueText string, and CONTAINS is what makes "does this advert have
                // option X among its selected values" work without knowing the delimiter the
                // frontend uses to join them. Requires the `containsFilter` experimental Meilisearch
                // feature - enabled idempotently by MeilisearchAdvertIndexService.ReindexAllAsync.
                var orClauses = af.ValueTextIn.Select(v => $"{field} CONTAINS {Quote(v)}");
                clauses.Add($"({string.Join(" OR ", orClauses)})");
            }
            else if (af.ValueNumberFrom.HasValue || af.ValueNumberTo.HasValue)
            {
                if (af.ValueNumberFrom.HasValue)
                    clauses.Add($"{field} >= {af.ValueNumberFrom.Value.ToString(CultureInfo.InvariantCulture)}");
                if (af.ValueNumberTo.HasValue)
                    clauses.Add($"{field} <= {af.ValueNumberTo.Value.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
    }

    // Meilisearch filter strings use double-quoted values; escape backslashes first, then quotes,
    // so a value containing either can't break out of the quoted literal or alter the expression.
    private static string Quote(string value)
    {
        var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }
}
