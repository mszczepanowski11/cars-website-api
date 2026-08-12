using CarsWebsite;
using cars_website_api.CarsWebsite.DTOs.Advert;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CarsWebsiteTests;

// Integration tests for AdvertService.SearchCarAdvertsAsync - specifically the duplicate-exclusion
// behavior added alongside cross-source deduplication (CTO audit Etap 2). A flagged duplicate must
// disappear from the general public search (so a buyer never sees the same car twice when it
// arrived via two overlapping partner feeds) while remaining fully visible in a user/partner's own
// "my adverts" view (dto.UserId set) - the canonical scenario this suite guards against regressing.
public class AdvertSearchTests
{
    private static async Task<(AppDbContext Context, IAdvertService AdvertService, int UserId)> SetupAsync(string testName)
    {
        var context = TestDbContextFactory.CreateContext(testName);
        var category = await TestDbContextFactory.SeedCategoryAsync(context);
        var user = await TestDbContextFactory.SeedBusinessUserAsync(context, $"{testName}@search.test");
        var advertService = TestDbContextFactory.CreateAdvertService(context);
        return (context, advertService, user.Id);
    }

    private static async Task<int> CreateAdvertAsync(IAdvertService service, int userId, string title, decimal price)
    {
        var dto = new CreateCarAdvertDto
        {
            Title = title,
            Description = "Testowy opis",
            Price = price,
            Condition = "used",
            SellerType = "dealer",
        };
        return await service.CreateCarAdvertAsync(dto, userId);
    }

    [Fact]
    public async Task SearchCarAdvertsAsync_PublicSearch_ExcludesAdvertFlaggedAsDuplicate()
    {
        var (context, advertService, userId) = await SetupAsync(nameof(SearchCarAdvertsAsync_PublicSearch_ExcludesAdvertFlaggedAsDuplicate));
        var canonicalId = await CreateAdvertAsync(advertService, userId, "Canonical listing", 50000);
        var duplicateId = await CreateAdvertAsync(advertService, userId, "Duplicate listing", 50500);
        var duplicate = await context.CarAdverts.FirstAsync(a => a.Id == duplicateId);
        duplicate.DuplicateOfId = canonicalId;
        duplicate.DuplicateMatchReason = "VIN";
        await context.SaveChangesAsync();

        var result = await advertService.SearchCarAdvertsAsync(new SearchCarAdvertDto { PageSize = 50 });

        var ids = result.Items.Select(i => i.Id).ToList();
        Assert.Contains(canonicalId, ids);
        Assert.DoesNotContain(duplicateId, ids);
    }

    [Fact]
    public async Task SearchCarAdvertsAsync_UserScopedSearch_StillShowsOwnDuplicateFlaggedAdvert()
    {
        var (context, advertService, userId) = await SetupAsync(nameof(SearchCarAdvertsAsync_UserScopedSearch_StillShowsOwnDuplicateFlaggedAdvert));
        var canonicalId = await CreateAdvertAsync(advertService, userId, "Canonical listing", 50000);
        var duplicateId = await CreateAdvertAsync(advertService, userId, "Duplicate listing", 50500);
        var duplicate = await context.CarAdverts.FirstAsync(a => a.Id == duplicateId);
        duplicate.DuplicateOfId = canonicalId;
        await context.SaveChangesAsync();

        var result = await advertService.SearchCarAdvertsAsync(new SearchCarAdvertDto { PageSize = 50, UserId = userId });

        var ids = result.Items.Select(i => i.Id).ToList();
        Assert.Contains(canonicalId, ids);
        Assert.Contains(duplicateId, ids); // a partner must still see everything they submitted
    }

    [Fact]
    public async Task SearchCarAdvertsAsync_PriceFilter_ExcludesOutOfRangeAdverts()
    {
        var (_, advertService, userId) = await SetupAsync(nameof(SearchCarAdvertsAsync_PriceFilter_ExcludesOutOfRangeAdverts));
        var cheapId = await CreateAdvertAsync(advertService, userId, "Tanie auto", 20000);
        var expensiveId = await CreateAdvertAsync(advertService, userId, "Drogie auto", 200000);

        var result = await advertService.SearchCarAdvertsAsync(new SearchCarAdvertDto { PageSize = 50, PriceFrom = 100000 });

        var ids = result.Items.Select(i => i.Id).ToList();
        Assert.Contains(expensiveId, ids);
        Assert.DoesNotContain(cheapId, ids);
    }
}
