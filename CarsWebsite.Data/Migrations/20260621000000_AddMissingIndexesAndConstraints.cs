using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddMissingIndexesAndConstraints : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // adverts — listing & user's adverts
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("adverts", "IX_Adverts_UserId", "`UserId`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("adverts", "IX_Adverts_IsActive_IsHidden", "`IsActive`, `IsHidden`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("adverts", "IX_Adverts_CreatedAt", "`CreatedAt`"));

            // caradverts — search filters (the heavy ones)
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("caradverts", "IX_CarAdverts_UserId", "`UserId`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("caradverts", "IX_CarAdverts_BrandId", "`BrandId`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("caradverts", "IX_CarAdverts_ModelId", "`ModelId`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("caradverts", "IX_CarAdverts_FuelTypeId", "`FuelTypeId`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("caradverts", "IX_CarAdverts_GearboxId", "`GearboxId`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("caradverts", "IX_CarAdverts_BodyTypeId", "`BodyTypeId`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("caradverts", "IX_CarAdverts_Mileage", "`Mileage`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("caradverts", "IX_CarAdverts_Badge", "`Badge`"));
            // Note: IX_CarAdverts_Price, IX_CarAdverts_Year, IX_CarAdverts_CreatedAt, IX_CarAdverts_VehicleCategoryId
            // were already added in AddPerformanceIndexes2 migration

            // reviews — duplicate prevention + listing
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("reviews", "UX_Reviews_BuyerId_SellerId", "`BuyerId`, `SellerId`", "UNIQUE"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("reviews", "IX_Reviews_SellerId", "`SellerId`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("reviews", "IX_Reviews_BuyerId", "`BuyerId`"));

            // messages — conversation threads + unread count
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("messages", "IX_Messages_ConversationId", "`ConversationId`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("messages", "IX_Messages_IsRead_SenderId", "`IsRead`, `SenderId`"));

            // refreshtokens — token lookup (auth critical)
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("refreshtokens", "UX_RefreshTokens_Token", "`Token`", "UNIQUE"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("refreshtokens", "IX_RefreshTokens_UserId", "`UserId`"));

            // payments
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("payments", "IX_Payments_UserId", "`UserId`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("payments", "IX_Payments_Status", "`Status`"));

            // favoriteadverts
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("favoriteadverts", "IX_FavoriteAdverts_UserId", "`UserId`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("favoriteadverts", "UX_FavoriteAdverts_UserId_AdvertId", "`UserId`, `AdvertId`", "UNIQUE"));

            // conversations
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("conversations", "IX_Conversations_BuyerId", "`BuyerId`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("conversations", "IX_Conversations_SellerId", "`SellerId`"));

            // appnotifications — unread badge count
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("appnotifications", "IX_AppNotifications_UserId_IsRead", "`UserId`, `IsRead`"));

            // newslettersubscribers — email lookup (already has UNIQUE from entity config, but ensure it)
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("newslettersubscribers", "UX_NewsletterSubscribers_Email", "`Email`", "UNIQUE"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("adverts", "IX_Adverts_UserId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("adverts", "IX_Adverts_IsActive_IsHidden"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("adverts", "IX_Adverts_CreatedAt"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("caradverts", "IX_CarAdverts_UserId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("caradverts", "IX_CarAdverts_BrandId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("caradverts", "IX_CarAdverts_ModelId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("caradverts", "IX_CarAdverts_FuelTypeId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("caradverts", "IX_CarAdverts_GearboxId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("caradverts", "IX_CarAdverts_BodyTypeId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("caradverts", "IX_CarAdverts_Mileage"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("caradverts", "IX_CarAdverts_Badge"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("reviews", "UX_Reviews_BuyerId_SellerId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("reviews", "IX_Reviews_SellerId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("reviews", "IX_Reviews_BuyerId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("messages", "IX_Messages_ConversationId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("messages", "IX_Messages_IsRead_SenderId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("refreshtokens", "UX_RefreshTokens_Token"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("refreshtokens", "IX_RefreshTokens_UserId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("payments", "IX_Payments_UserId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("payments", "IX_Payments_Status"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("favoriteadverts", "IX_FavoriteAdverts_UserId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("favoriteadverts", "UX_FavoriteAdverts_UserId_AdvertId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("conversations", "IX_Conversations_BuyerId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("conversations", "IX_Conversations_SellerId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("appnotifications", "IX_AppNotifications_UserId_IsRead"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("newslettersubscribers", "UX_NewsletterSubscribers_Email"));
        }
    }
}
