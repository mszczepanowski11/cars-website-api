using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddSubscriptionToUsers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("users", "SubscriptionTier", "INT NOT NULL DEFAULT 0"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("users", "SubscriptionExpiresAt", "DATETIME(6) NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("users", "SubscriptionStartedAt", "DATETIME(6) NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("users", "StartProgramActivatedAt", "DATETIME(6) NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("users", "FeaturedQuotaUsed", "INT NOT NULL DEFAULT 0"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("users", "FeaturedQuotaResetAt", "DATETIME(6) NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("users", "IsVerifiedDealer", "TINYINT(1) NOT NULL DEFAULT 0"));

            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("users", "IX_users_SubscriptionTier", "`SubscriptionTier`"));

            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("users", "IX_users_SubscriptionExpiresAt", "`SubscriptionExpiresAt`"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("users", "SubscriptionTier"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("users", "SubscriptionExpiresAt"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("users", "SubscriptionStartedAt"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("users", "StartProgramActivatedAt"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("users", "FeaturedQuotaUsed"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("users", "FeaturedQuotaResetAt"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("users", "IsVerifiedDealer"));
        }
    }
}
