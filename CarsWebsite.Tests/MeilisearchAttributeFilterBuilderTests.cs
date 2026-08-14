using System.Collections.Generic;
using cars_website_api.CarsWebsite.DTOs.Advert;
using cars_website_api.CarsWebsite.Services;
using Xunit;

namespace CarsWebsiteTests;

// Unit tests for the pure filter-string-building logic that routes SearchCarAdvertDto.AttributeFilters
// through Meilisearch (CTO audit Etap 4 attribute-filter pass). No DB/network involved - these pin
// down the exact filter-expression syntax and escaping, which is easy to get subtly wrong and hard
// to notice from an integration test alone (a bad escape only breaks on a value containing a quote).
public class MeilisearchAttributeFilterBuilderTests
{
    [Fact]
    public void Build_NullOrEmptyList_ReturnsNull()
    {
        Assert.Null(MeilisearchAttributeFilterBuilder.Build(null));
        Assert.Null(MeilisearchAttributeFilterBuilder.Build(new List<AttributeFilterDto>()));
    }

    [Fact]
    public void Build_BoolFilter_TrueAndFalse()
    {
        Assert.Equal("attr_5 = true", MeilisearchAttributeFilterBuilder.Build(new List<AttributeFilterDto>
        {
            new() { AttributeDefinitionId = 5, ValueBool = true },
        }));
        Assert.Equal("attr_5 = false", MeilisearchAttributeFilterBuilder.Build(new List<AttributeFilterDto>
        {
            new() { AttributeDefinitionId = 5, ValueBool = false },
        }));
    }

    [Fact]
    public void Build_NumberRange_BothBoundsPresent()
    {
        var result = MeilisearchAttributeFilterBuilder.Build(new List<AttributeFilterDto>
        {
            new() { AttributeDefinitionId = 20, ValueNumberFrom = 100, ValueNumberTo = 200 },
        });
        Assert.Equal("attr_20 >= 100 AND attr_20 <= 200", result);
    }

    [Fact]
    public void Build_NumberRange_OnlyFromBound()
    {
        var result = MeilisearchAttributeFilterBuilder.Build(new List<AttributeFilterDto>
        {
            new() { AttributeDefinitionId = 20, ValueNumberFrom = 100 },
        });
        Assert.Equal("attr_20 >= 100", result);
    }

    [Fact]
    public void Build_NumberRange_OnlyToBound()
    {
        var result = MeilisearchAttributeFilterBuilder.Build(new List<AttributeFilterDto>
        {
            new() { AttributeDefinitionId = 20, ValueNumberTo = 200 },
        });
        Assert.Equal("attr_20 <= 200", result);
    }

    [Fact]
    public void Build_TextIn_SingleValue_UsesContains()
    {
        var result = MeilisearchAttributeFilterBuilder.Build(new List<AttributeFilterDto>
        {
            new() { AttributeDefinitionId = 8, ValueTextIn = new List<string> { "Skóra" } },
        });
        Assert.Equal("(attr_8 CONTAINS \"Skóra\")", result);
    }

    [Fact]
    public void Build_TextIn_MultipleValues_OrsThem()
    {
        var result = MeilisearchAttributeFilterBuilder.Build(new List<AttributeFilterDto>
        {
            new() { AttributeDefinitionId = 8, ValueTextIn = new List<string> { "Skóra", "Welur" } },
        });
        Assert.Equal("(attr_8 CONTAINS \"Skóra\" OR attr_8 CONTAINS \"Welur\")", result);
    }

    [Fact]
    public void Build_TextIn_EscapesQuotesAndBackslashes()
    {
        var result = MeilisearchAttributeFilterBuilder.Build(new List<AttributeFilterDto>
        {
            new() { AttributeDefinitionId = 8, ValueTextIn = new List<string> { "Say \"hi\" \\ now" } },
        });
        Assert.Equal("(attr_8 CONTAINS \"Say \\\"hi\\\" \\\\ now\")", result);
    }

    [Fact]
    public void Build_MultipleFilters_AndsThemTogether()
    {
        var result = MeilisearchAttributeFilterBuilder.Build(new List<AttributeFilterDto>
        {
            new() { AttributeDefinitionId = 5, ValueBool = true },
            new() { AttributeDefinitionId = 20, ValueNumberFrom = 100 },
            new() { AttributeDefinitionId = 8, ValueTextIn = new List<string> { "Skóra" } },
        });
        Assert.Equal("attr_5 = true AND attr_20 >= 100 AND (attr_8 CONTAINS \"Skóra\")", result);
    }
}
