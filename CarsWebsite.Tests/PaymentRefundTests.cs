using CarsWebsite;
using cars_website_api.CarsWebsite.Domain.Entities;
using cars_website_api.CarsWebsite.DTOs.Payment;
using cars_website_api.CarsWebsite.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CarsWebsiteTests;

// A no-op fire-and-forget sink - PaymentService calls NotifyAsync without awaiting it
// (`_ = _notifications.NotifyAsync(...)`), so tests only need a working interface implementation,
// not a real email pipeline.
public class NullNotificationService : INotificationService
{
    public Task NotifyAsync(int userId, EmailNotificationType type, string title, string content,
        int? advertId = null, int? paymentId = null, int? invoiceId = null) => Task.CompletedTask;
}

// Integration tests for the refund → entitlement-revocation path (CTO audit Etap 3 - "dziś
// Refunded to martwy status, entitlement zostaje aktywny"). Drives PaymentService.HandleWebhookAsync
// exactly as imoje's real webhook would, using the InternalServiceSecret bypass (see VerifySignature)
// instead of computing a real HMAC signature - this exercises the actual production code path
// (transaction locking, status transitions, ActivateServiceAsync/RevokeServiceAsync), not a
// reimplementation of it.
public class PaymentRefundTests
{
    private const string InternalSecret = "test-internal-secret";

    private static async Task<(AppDbContext Context, IPaymentService Service, User User)> SetupAsync(string testName)
    {
        // Real MySQL, not InMemory - HandleWebhookAsync's row lock (ExecuteSqlRawAsync ... FOR
        // UPDATE) and explicit transaction have no InMemory-provider equivalent (see
        // TestDbContextFactory.CreateMySqlContextAsync).
        var context = await TestDbContextFactory.CreateMySqlContextAsync(testName);
        var user = await TestDbContextFactory.SeedBusinessUserAsync(context, $"{testName}@refund.test");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["InternalServiceSecret"] = InternalSecret })
            .Build();
        var subscriptionService = new SubscriptionService(context, NullLogger<SubscriptionService>.Instance);
        var paymentService = new PaymentService(context, config, new NullNotificationService(), subscriptionService, NullLogger<PaymentService>.Instance, new RecordingBackgroundJobClient());
        return (context, paymentService, user);
    }

    private static async Task<Payment> CreatePendingAdvertPaymentAsync(AppDbContext context, User user, CarAdvert advert,
        ServiceType serviceType, int durationDays, string orderId)
    {
        var payment = new Payment
        {
            UserId = user.Id,
            AdvertId = advert.Id,
            ServiceType = serviceType,
            ServiceDescription = "Test service",
            Amount = 29.99m,
            DurationDays = durationDays,
            Status = PaymentStatus.Pending,
            ImojeOrderId = orderId,
        };
        context.Payments.Add(payment);
        await context.SaveChangesAsync();
        return payment;
    }

    private static async Task<CarAdvert> CreateAdvertAsync(AppDbContext context, int userId)
    {
        var advert = new CarAdvert
        {
            UserId = userId,
            Title = "Test advert",
            Description = "desc",
            Price = 10000,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        context.CarAdverts.Add(advert);
        await context.SaveChangesAsync();
        return advert;
    }

    [Fact]
    public async Task Refund_AfterCompletedTopBadge_RemovesTheBadge()
    {
        var (context, service, user) = await SetupAsync(nameof(Refund_AfterCompletedTopBadge_RemovesTheBadge));
        var advert = await CreateAdvertAsync(context, user.Id);
        var payment = await CreatePendingAdvertPaymentAsync(context, user, advert, ServiceType.Top, 30, "ORDER-TOP-1");

        await service.HandleWebhookAsync(new ImojeWebhookDto { OrderId = "ORDER-TOP-1", Status = "settled" }, "{}", "", InternalSecret);
        var afterActivation = await context.CarAdverts.AsNoTracking().FirstAsync(a => a.Id == advert.Id);
        Assert.Equal("TOP", afterActivation.Badge);
        Assert.NotNull(afterActivation.BadgeExpiresAt);

        await service.HandleWebhookAsync(new ImojeWebhookDto { OrderId = "ORDER-TOP-1", Status = "Refunded" }, "{}", "", InternalSecret);

        var afterRefund = await context.CarAdverts.AsNoTracking().FirstAsync(a => a.Id == advert.Id);
        Assert.Null(afterRefund.Badge);
        Assert.Null(afterRefund.BadgeExpiresAt);
        var paymentRow = await context.Payments.AsNoTracking().FirstAsync(p => p.Id == payment.Id);
        Assert.Equal(PaymentStatus.Refunded, paymentRow.Status);
    }

    [Fact]
    public async Task Refund_AfterCompletedSubscription_ResetsUserToNoTier()
    {
        var (context, service, user) = await SetupAsync(nameof(Refund_AfterCompletedSubscription_ResetsUserToNoTier));
        var payment = new Payment
        {
            UserId = user.Id,
            ServiceType = ServiceType.Subscription,
            SubscriptionTier = SubscriptionTier.Biznes,
            ServiceDescription = "Pakiet Biznes",
            Amount = 99.99m,
            DurationDays = 30,
            Status = PaymentStatus.Pending,
            ImojeOrderId = "ORDER-SUB-1",
        };
        context.Payments.Add(payment);
        await context.SaveChangesAsync();

        await service.HandleWebhookAsync(new ImojeWebhookDto { OrderId = "ORDER-SUB-1", Status = "settled" }, "{}", "", InternalSecret);
        var afterActivation = await context.Users.AsNoTracking().FirstAsync(u => u.Id == user.Id);
        Assert.Equal(SubscriptionTier.Biznes, afterActivation.SubscriptionTier);
        Assert.NotNull(afterActivation.SubscriptionExpiresAt);

        await service.HandleWebhookAsync(new ImojeWebhookDto { OrderId = "ORDER-SUB-1", Status = "Refunded" }, "{}", "", InternalSecret);

        var afterRefund = await context.Users.AsNoTracking().FirstAsync(u => u.Id == user.Id);
        Assert.Equal(SubscriptionTier.None, afterRefund.SubscriptionTier);
        Assert.Null(afterRefund.SubscriptionExpiresAt);
    }

    [Fact]
    public async Task Refund_OnPaymentThatNeverCompleted_JustMarksRefundedWithoutTouchingAnything()
    {
        var (context, service, user) = await SetupAsync(nameof(Refund_OnPaymentThatNeverCompleted_JustMarksRefundedWithoutTouchingAnything));
        var advert = await CreateAdvertAsync(context, user.Id);
        await CreatePendingAdvertPaymentAsync(context, user, advert, ServiceType.Top, 30, "ORDER-NEVER-1");

        await service.HandleWebhookAsync(new ImojeWebhookDto { OrderId = "ORDER-NEVER-1", Status = "Refunded" }, "{}", "", InternalSecret);

        var afterRefund = await context.CarAdverts.AsNoTracking().FirstAsync(a => a.Id == advert.Id);
        Assert.Null(afterRefund.Badge); // was never granted, refund must not throw trying to "revoke" nothing
        var paymentRow = await context.Payments.AsNoTracking().FirstAsync(p => p.ImojeOrderId == "ORDER-NEVER-1");
        Assert.Equal(PaymentStatus.Refunded, paymentRow.Status);
    }

    [Fact]
    public async Task Refund_DuplicateWebhook_DoesNotDoubleRevoke()
    {
        var (context, service, user) = await SetupAsync(nameof(Refund_DuplicateWebhook_DoesNotDoubleRevoke));
        var advert = await CreateAdvertAsync(context, user.Id);
        await CreatePendingAdvertPaymentAsync(context, user, advert, ServiceType.Top, 30, "ORDER-DUP-1");

        await service.HandleWebhookAsync(new ImojeWebhookDto { OrderId = "ORDER-DUP-1", Status = "settled" }, "{}", "", InternalSecret);
        await service.HandleWebhookAsync(new ImojeWebhookDto { OrderId = "ORDER-DUP-1", Status = "Refunded" }, "{}", "", InternalSecret);
        var afterFirstRefund = await context.CarAdverts.AsNoTracking().FirstAsync(a => a.Id == advert.Id);
        Assert.Null(afterFirstRefund.Badge);

        // A second, duplicate Refunded webhook for the same order (imoje retries delivery) must be
        // a no-op, not try to subtract another 30 days from an already-null BadgeExpiresAt.
        await service.HandleWebhookAsync(new ImojeWebhookDto { OrderId = "ORDER-DUP-1", Status = "Refunded" }, "{}", "", InternalSecret);
        var afterSecondRefund = await context.CarAdverts.AsNoTracking().FirstAsync(a => a.Id == advert.Id);
        Assert.Null(afterSecondRefund.Badge);
        Assert.Null(afterSecondRefund.BadgeExpiresAt);
    }
}
