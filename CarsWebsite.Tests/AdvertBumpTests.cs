using CarsWebsite;
using cars_website_api.CarsWebsite.DTOs.Advert;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CarsWebsiteTests;

// Regresja zgloszona z produkcji: zapisanie zmian w ogloszeniu wypychalo je na gora listy
// „Najnowsze", czyli dawalo dokladnie to, za co sprzedajacy ma zaplacic w usludze
// „Odswiezenie". Przyczyna: sortowanie domyslne szlo po UpdatedAt, a UpdatedAt ustawia sie
// przy KAZDYM zapisie ogloszenia (AdvertMappingProfile robi to w mapowaniu). Wystarczylo
// poprawic przecinek w opisie, zeby ogloszenie wrocilo na gore - i mozna to bylo powtarzac
// bez konca.
//
// Od tej zmiany o kolejnosci decyduje BumpedAt, ktore ustawia sie tylko wtedy, gdy
// ogloszenie ZASLUZENIE trafia na gore: przy utworzeniu, publikacji, odnowieniu i przy
// oplaconym odswiezeniu.
public class AdvertBumpTests
{
    private static async Task<(AppDbContext Context, IAdvertService AdvertService, int UserId)> SetupAsync(string testName)
    {
        var context = TestDbContextFactory.CreateContext(testName);
        var user = await TestDbContextFactory.SeedBusinessUserAsync(context, $"{testName}@bump.test");
        var advertService = TestDbContextFactory.CreateAdvertService(context);
        return (context, advertService, user.Id);
    }

    private static async Task<int> CreateAsync(IAdvertService service, int userId, string title) =>
        await service.CreateCarAdvertAsync(new CreateCarAdvertDto
        {
            Title = title,
            Description = "Testowy opis",
            Price = 50000,
            Condition = "used",
            SellerType = "dealer",
        }, userId);

    private static UpdateCarAdvertDto EditDto(string title, decimal price) => new()
    {
        Title = title,
        Description = "Opis po poprawce",
        Price = price,
        Condition = "used",
        SellerType = "dealer",
    };

    // To jest ten blad: zapis ogloszenia NIE moze byc darmowym odswiezeniem.
    [Fact]
    public async Task UpdateCarAdvertAsync_DoesNotBumpAdvertToTop()
    {
        var (context, service, userId) = await SetupAsync(nameof(UpdateCarAdvertAsync_DoesNotBumpAdvertToTop));
        var advertId = await CreateAsync(service, userId, "Ogloszenie testowe");

        var beforeEdit = await context.CarAdverts.AsNoTracking().FirstAsync(a => a.Id == advertId);
        var bumpedBefore = beforeEdit.BumpedAt;
        Assert.NotNull(bumpedBefore);

        await service.UpdateCarAdvertAsync(advertId, EditDto("Ogloszenie testowe po zmianie", 50000), userId);

        var afterEdit = await context.CarAdverts.AsNoTracking().FirstAsync(a => a.Id == advertId);
        Assert.Equal(bumpedBefore, afterEdit.BumpedAt);
    }

    // Druga polowa tej samej zasady: UpdatedAt MA sie zmieniac, bo znaczy „tresc sie zmienila"
    // i z tego zyje data aktualizacji oraz lastmod w mapie strony. Gdyby ktos „naprawil" ten
    // blad, zabierajac UpdatedAt z mapowania, zepsulby jedno i drugie.
    [Fact]
    public async Task UpdateCarAdvertAsync_StillMarksContentAsUpdated()
    {
        var (context, service, userId) = await SetupAsync(nameof(UpdateCarAdvertAsync_StillMarksContentAsUpdated));
        var advertId = await CreateAsync(service, userId, "Ogloszenie testowe");

        await service.UpdateCarAdvertAsync(advertId, EditDto("Nowy tytul", 48000), userId);

        var afterEdit = await context.CarAdverts.AsNoTracking().FirstAsync(a => a.Id == advertId);
        Assert.NotNull(afterEdit.UpdatedAt);
        Assert.Equal("Nowy tytul", afterEdit.Title);
    }

    // Odnowienie to powrot ogloszenia do obiegu - tu wypchniecie na gore jest zasluzone.
    [Fact]
    public async Task RenewAsync_BumpsAdvertToTop()
    {
        var (context, service, userId) = await SetupAsync(nameof(RenewAsync_BumpsAdvertToTop));
        var advertId = await CreateAsync(service, userId, "Ogloszenie do odnowienia");

        var beforeRenew = await context.CarAdverts.AsNoTracking().FirstAsync(a => a.Id == advertId);
        var bumpedBefore = beforeRenew.BumpedAt;

        await service.RenewAsync(advertId, userId);

        var afterRenew = await context.CarAdverts.AsNoTracking().FirstAsync(a => a.Id == advertId);
        Assert.NotNull(afterRenew.BumpedAt);
        Assert.True(afterRenew.BumpedAt >= bumpedBefore);
    }

    // Ta sama pulapka, ktora juz raz kosztowala darmowe 35 dni emisji (AdvertPublishTests):
    // strona promowania wola publish przy KAZDYM wejsciu. Samo jej otwarcie nie moze
    // wypychac ogloszenia na gore listy.
    [Fact]
    public async Task PublishAsync_OnAlreadyActiveAdvert_DoesNotBumpAgain()
    {
        var (context, service, userId) = await SetupAsync(nameof(PublishAsync_OnAlreadyActiveAdvert_DoesNotBumpAgain));
        var advertId = await CreateAsync(service, userId, "Ogloszenie aktywne");

        await service.PublishAsync(advertId, userId);
        var afterFirstPublish = await context.CarAdverts.AsNoTracking().FirstAsync(a => a.Id == advertId);
        var bumpedAfterFirstPublish = afterFirstPublish.BumpedAt;
        Assert.NotNull(bumpedAfterFirstPublish);

        await service.PublishAsync(advertId, userId);

        var afterSecondPublish = await context.CarAdverts.AsNoTracking().FirstAsync(a => a.Id == advertId);
        Assert.Equal(bumpedAfterFirstPublish, afterSecondPublish.BumpedAt);
    }
}
