using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddSubscriptionToUsers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE `users`
                    ADD COLUMN IF NOT EXISTS `SubscriptionTier`          INT          NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS `SubscriptionExpiresAt`      DATETIME(6)  NULL,
                    ADD COLUMN IF NOT EXISTS `SubscriptionStartedAt`      DATETIME(6)  NULL,
                    ADD COLUMN IF NOT EXISTS `StartProgramActivatedAt`    DATETIME(6)  NULL,
                    ADD COLUMN IF NOT EXISTS `FeaturedQuotaUsed`          INT          NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS `FeaturedQuotaResetAt`       DATETIME(6)  NULL,
                    ADD COLUMN IF NOT EXISTS `IsVerifiedDealer`           TINYINT(1)   NOT NULL DEFAULT 0;
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS `IX_users_SubscriptionTier`
                    ON `users` (`SubscriptionTier`);
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS `IX_users_SubscriptionExpiresAt`
                    ON `users` (`SubscriptionExpiresAt`);
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE `users`
                    DROP COLUMN IF EXISTS `SubscriptionTier`,
                    DROP COLUMN IF EXISTS `SubscriptionExpiresAt`,
                    DROP COLUMN IF EXISTS `SubscriptionStartedAt`,
                    DROP COLUMN IF EXISTS `StartProgramActivatedAt`,
                    DROP COLUMN IF EXISTS `FeaturedQuotaUsed`,
                    DROP COLUMN IF EXISTS `FeaturedQuotaResetAt`,
                    DROP COLUMN IF EXISTS `IsVerifiedDealer`;
            ");
        }
    }
}
