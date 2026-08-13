using CarsWebsite;
using cars_website_api.CarsWebsite.DTOs.Advert;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CarsWebsiteTests;

// Bulk self-serve tooling for dealers with dozens/hundreds of adverts (CTO audit Etap 3).
public class AdvertBulkActionTests
{
    private static async Task<(AppDbContext Context, IAdvertService AdvertService, int UserId)> SetupAsync(string testName)
    {
        var context = TestDbContextFactory.CreateContext(testName);
        var user = await TestDbContextFactory.SeedBusinessUserAsync(context, $"{testName}@bulk.test");
        var advertService = TestDbContextFactory.CreateAdvertService(context);
        return (context, advertService, user.Id);
    }

    private static async Task<int> CreateAdvertAsync(IAdvertService service, int userId, string title, decimal price = 10000)
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
    public async Task BulkAction_Deactivate_AllOwnedAdverts_AllSucceed()
    {
        var (context, service, userId) = await SetupAsync(nameof(BulkAction_Deactivate_AllOwnedAdverts_AllSucceed));
        var id1 = await CreateAdvertAsync(service, userId, "Auto 1");
        var id2 = await CreateAdvertAsync(service, userId, "Auto 2");

        var result = await service.BulkActionAsync(new List<int> { id1, id2 }, "deactivate", userId);

        Assert.Equal(new[] { id1, id2 }, result.Succeeded.OrderBy(x => x));
        Assert.Empty(result.Failed);
        var adverts = await context.CarAdverts.AsNoTracking().Where(a => a.UserId == userId).ToListAsync();
        Assert.All(adverts, a => Assert.False(a.IsActive));
    }

    [Fact]
    public async Task BulkAction_IncludesAdvertNotOwnedByCaller_ReportsFailureWithoutBlockingOthers()
    {
        var (context, service, userId) = await SetupAsync(nameof(BulkAction_IncludesAdvertNotOwnedByCaller_ReportsFailureWithoutBlockingOthers));
        var otherUser = await TestDbContextFactory.SeedBusinessUserAsync(context, "other@bulk.test");
        var myId = await CreateAdvertAsync(service, userId, "Moje auto");
        var otherId = await CreateAdvertAsync(service, otherUser.Id, "Cudze auto");

        var result = await service.BulkActionAsync(new List<int> { myId, otherId }, "deactivate", userId);

        Assert.Single(result.Succeeded, myId);
        var failure = Assert.Single(result.Failed);
        Assert.Equal(otherId, failure.Id);

        var mine = await context.CarAdverts.AsNoTracking().FirstAsync(a => a.Id == myId);
        var theirs = await context.CarAdverts.AsNoTracking().FirstAsync(a => a.Id == otherId);
        Assert.False(mine.IsActive);
        Assert.True(theirs.IsActive); // untouched
    }

    [Fact]
    public async Task BulkAction_MarkSold_TransitionsAdvertsToSold()
    {
        var (context, service, userId) = await SetupAsync(nameof(BulkAction_MarkSold_TransitionsAdvertsToSold));
        var id1 = await CreateAdvertAsync(service, userId, "Auto 1");

        var result = await service.BulkActionAsync(new List<int> { id1 }, "markSold", userId);

        Assert.Single(result.Succeeded);
        var advert = await context.CarAdverts.AsNoTracking().FirstAsync(a => a.Id == id1);
        Assert.NotNull(advert.SoldAt);
        Assert.False(advert.IsActive);
    }

    [Fact]
    public async Task BulkAction_Activate_AfterDeactivate_ReactivatesAdverts()
    {
        var (context, service, userId) = await SetupAsync(nameof(BulkAction_Activate_AfterDeactivate_ReactivatesAdverts));
        var id1 = await CreateAdvertAsync(service, userId, "Auto 1");
        await service.BulkActionAsync(new List<int> { id1 }, "deactivate", userId);

        var result = await service.BulkActionAsync(new List<int> { id1 }, "activate", userId);

        Assert.Single(result.Succeeded);
        var advert = await context.CarAdverts.AsNoTracking().FirstAsync(a => a.Id == id1);
        Assert.True(advert.IsActive);
    }

    [Fact]
    public async Task BulkAction_UnknownAction_Throws()
    {
        var (_, service, userId) = await SetupAsync(nameof(BulkAction_UnknownAction_Throws));
        var id1 = await CreateAdvertAsync(service, userId, "Auto 1");

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.BulkActionAsync(new List<int> { id1 }, "nuke", userId));
    }

    [Fact]
    public async Task BulkAction_EmptyIds_Throws()
    {
        var (_, service, userId) = await SetupAsync(nameof(BulkAction_EmptyIds_Throws));

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.BulkActionAsync(new List<int>(), "deactivate", userId));
    }

    [Fact]
    public async Task BulkAction_ExceedsMaxBatchSize_Throws()
    {
        var (_, service, userId) = await SetupAsync(nameof(BulkAction_ExceedsMaxBatchSize_Throws));
        var ids = Enumerable.Range(1, 201).ToList();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.BulkActionAsync(ids, "deactivate", userId));
    }

    [Fact]
    public async Task ExportUserAdvertsCsv_ContainsHeaderAndRowsWithCommaEscaping()
    {
        var (_, service, userId) = await SetupAsync(nameof(ExportUserAdvertsCsv_ContainsHeaderAndRowsWithCommaEscaping));
        await CreateAdvertAsync(service, userId, "BMW 3, Series");

        var csv = await service.ExportUserAdvertsCsvAsync(userId);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("Id,Tytul,Marka,Model,Rok,Cena,Waluta,Przebieg,VIN,Status,Wyroznienie,DataDodania,WygasaData", lines[0]);
        Assert.Contains("\"BMW 3, Series\"", lines[1]);
        Assert.Contains("Aktywne", lines[1]);
    }

    [Fact]
    public async Task ExportUserAdvertsCsv_DoesNotIncludeOtherUsersAdverts()
    {
        var (context, service, userId) = await SetupAsync(nameof(ExportUserAdvertsCsv_DoesNotIncludeOtherUsersAdverts));
        var otherUser = await TestDbContextFactory.SeedBusinessUserAsync(context, "other2@bulk.test");
        await CreateAdvertAsync(service, userId, "Moje auto");
        await CreateAdvertAsync(service, otherUser.Id, "Cudze auto");

        var csv = await service.ExportUserAdvertsCsvAsync(userId);

        Assert.Contains("Moje auto", csv);
        Assert.DoesNotContain("Cudze auto", csv);
    }
}
