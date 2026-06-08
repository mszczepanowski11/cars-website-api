namespace CarsWebsite;

public enum EmailNotificationType
{
    // Registration
    AccountCreated,
    PasswordChanged,
    PasswordReset,

    // Adverts
    AdvertAdded,
    AdvertPublished,
    AdvertRejected,
    AdvertDeleted,
    AdvertMarkedSold,

    // Advert expiry
    AdvertExpiring7Days,
    AdvertExpiring3Days,
    AdvertExpiring1Day,
    AdvertExpired,

    // Promotions
    PromotionPurchased,
    PromotionActivated,
    TopStarted,
    PremiumStarted,
    FeaturedStarted,
    RefreshStarted,

    // Promotion expiry
    PromotionExpiring3Days,
    PromotionExpiring1Day,
    PromotionExpired,

    // Payments
    PaymentConfirmed,
    PaymentFailed,
    PaymentRefunded,

    // Invoices
    InvoiceGenerated,
    InvoiceSent,

    // Messages
    NewMessage,
}
