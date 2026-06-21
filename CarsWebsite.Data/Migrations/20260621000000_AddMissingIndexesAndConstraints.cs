using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddMissingIndexesAndConstraints : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // adverts — listing & user's adverts
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_Adverts_UserId` ON `adverts` (`UserId`)");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_Adverts_IsActive_IsHidden` ON `adverts` (`IsActive`, `IsHidden`)");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_Adverts_CreatedAt` ON `adverts` (`CreatedAt`)");

            // caradverts — search filters (the heavy ones)
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_CarAdverts_UserId` ON `caradverts` (`UserId`)");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_CarAdverts_BrandId` ON `caradverts` (`BrandId`)");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_CarAdverts_ModelId` ON `caradverts` (`ModelId`)");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_CarAdverts_FuelTypeId` ON `caradverts` (`FuelTypeId`)");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_CarAdverts_GearboxId` ON `caradverts` (`GearboxId`)");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_CarAdverts_BodyTypeId` ON `caradverts` (`BodyTypeId`)");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_CarAdverts_Mileage` ON `caradverts` (`Mileage`)");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_CarAdverts_Badge` ON `caradverts` (`Badge`)");
            // Note: IX_CarAdverts_Price, IX_CarAdverts_Year, IX_CarAdverts_CreatedAt, IX_CarAdverts_VehicleCategoryId
            // were already added in AddPerformanceIndexes2 migration

            // reviews — duplicate prevention + listing
            migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS `UX_Reviews_BuyerId_SellerId` ON `reviews` (`BuyerId`, `SellerId`)");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_Reviews_SellerId` ON `reviews` (`SellerId`)");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_Reviews_BuyerId` ON `reviews` (`BuyerId`)");

            // messages — conversation threads + unread count
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_Messages_ConversationId` ON `messages` (`ConversationId`)");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_Messages_IsRead_SenderId` ON `messages` (`IsRead`, `SenderId`)");

            // refreshtokens — token lookup (auth critical)
            migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS `UX_RefreshTokens_Token` ON `refreshtokens` (`Token`)");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_RefreshTokens_UserId` ON `refreshtokens` (`UserId`)");

            // payments
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_Payments_UserId` ON `payments` (`UserId`)");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_Payments_Status` ON `payments` (`Status`)");

            // favoriteadverts
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_FavoriteAdverts_UserId` ON `favoriteadverts` (`UserId`)");
            migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS `UX_FavoriteAdverts_UserId_AdvertId` ON `favoriteadverts` (`UserId`, `AdvertId`)");

            // conversations
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_Conversations_BuyerId` ON `conversations` (`BuyerId`)");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_Conversations_SellerId` ON `conversations` (`SellerId`)");

            // appnotifications — unread badge count
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_AppNotifications_UserId_IsRead` ON `appnotifications` (`UserId`, `IsRead`)");

            // newslettersubscribers — email lookup (already has UNIQUE from entity config, but ensure it)
            migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS `UX_NewsletterSubscribers_Email` ON `newslettersubscribers` (`Email`)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_Adverts_UserId` ON `adverts`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_Adverts_IsActive_IsHidden` ON `adverts`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_Adverts_CreatedAt` ON `adverts`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_CarAdverts_UserId` ON `caradverts`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_CarAdverts_BrandId` ON `caradverts`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_CarAdverts_ModelId` ON `caradverts`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_CarAdverts_FuelTypeId` ON `caradverts`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_CarAdverts_GearboxId` ON `caradverts`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_CarAdverts_BodyTypeId` ON `caradverts`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_CarAdverts_Mileage` ON `caradverts`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_CarAdverts_Badge` ON `caradverts`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `UX_Reviews_BuyerId_SellerId` ON `reviews`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_Reviews_SellerId` ON `reviews`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_Reviews_BuyerId` ON `reviews`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_Messages_ConversationId` ON `messages`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_Messages_IsRead_SenderId` ON `messages`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `UX_RefreshTokens_Token` ON `refreshtokens`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_RefreshTokens_UserId` ON `refreshtokens`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_Payments_UserId` ON `payments`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_Payments_Status` ON `payments`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_FavoriteAdverts_UserId` ON `favoriteadverts`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `UX_FavoriteAdverts_UserId_AdvertId` ON `favoriteadverts`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_Conversations_BuyerId` ON `conversations`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_Conversations_SellerId` ON `conversations`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_AppNotifications_UserId_IsRead` ON `appnotifications`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `UX_NewsletterSubscribers_Email` ON `newslettersubscribers`");
        }
    }
}
