using CarsWebsite;
using cars_website_api.CarsWebsite.DTOs.Advert;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CarsWebsiteTests;

// Regression coverage for a production bug: the promote-advert payment page calls POST
// .../publish on every visit to "make sure the advert is active" before letting the seller pay to
// promote it (see promote-advert/[id].vue's initiatePayment). PublishAsync used to unconditionally
// reset ExpiresAt every time it ran, so simply opening the promotion page - without ever completing
// a payment - silently gave the advert a free extra 35 days of emission. PublishAsync must now only
// touch ExpiresAt on a genuine inactive/expired -> active transition.
public class AdvertPublishTests
{
    private static async Task<(AppDbContext Context, IAdvertService AdvertService, int UserId)> SetupAsync(string testName)
    {
        var context = TestDbContextFactory.CreateContext(testName);
        var user = await TestDbContextFactory.SeedBusinessUserAsync(context, $"{testName}@publish.test");
        var advertService = TestDbContextFactory.CreateAdvertService(context);
        return (context, advertService, user.Id);
    }

    [Fact]
    public async Task PublishAsync_OnAlreadyActiveAdvert_DoesNotResetExpiresAt()
    {
        var (context, service, userId) = await SetupAsync(nameof(PublishAsync_OnAlreadyActiveAdvert_DoesNotResetExpiresAt));
        var advertId = await service.CreateCarAdvertAsync(new CreateCarAdvertDto
        {
            Title = "Publish idempotency test",
            Description = "Testowy opis",
            Price = 50000,
            Condition = "used",
            SellerType = "dealer",
        }, userId);

        // First publish: advert was created inactive, this is a genuine activation.
        await service.PublishAsync(advertId, userId);
        var afterFirstPublish = await context.CarAdverts.AsNoTracking().FirstAsync(a => a.Id == advertId);
        Assert.True(afterFirstPublish.IsActive);
        var expiresAtAfterFirstPublish = afterFirstPublish.ExpiresAt;
        Assert.NotNull(expiresAtAfterFirstPublish);

        // Simulate the promote-advert page's defensive re-publish call on a later visit, once the
        // advert is already active - this must be a no-op regarding ExpiresAt.
        await service.PublishAsync(advertId, userId);
        var afterSecondPublish = await context.CarAdverts.AsNoTracking().FirstAsync(a => a.Id == advertId);
        Assert.Equal(expiresAtAfterFirstPublish, afterSecondPublish.ExpiresAt);
    }

    [Fact]
    public async Task PublishAsync_OnInactiveAdvert_SetsFreshExpiresAt()
    {
        var (context, service, userId) = await SetupAsync(nameof(PublishAsync_OnInactiveAdvert_SetsFreshExpiresAt));
        var advertId = await service.CreateCarAdvertAsync(new CreateCarAdvertDto
        {
            Title = "Republish after deactivation test",
            Description = "Testowy opis",
            Price = 40000,
            Condition = "used",
            SellerType = "dealer",
        }, userId);
        await service.PublishAsync(advertId, userId);
        await service.DeactivateAsync(advertId, userId);

        var beforeRepublish = await context.CarAdverts.AsNoTracking().FirstAsync(a => a.Id == advertId);
        Assert.False(beforeRepublish.IsActive);

        await service.PublishAsync(advertId, userId);

        var afterRepublish = await context.CarAdverts.AsNoTracking().FirstAsync(a => a.Id == advertId);
        Assert.True(afterRepublish.IsActive);
        Assert.True(afterRepublish.ExpiresAt > DateTime.UtcNow.AddDays(30));
    }
}
