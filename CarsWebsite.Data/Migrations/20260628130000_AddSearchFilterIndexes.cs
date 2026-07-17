using Microsoft.EntityFrameworkCore.Migrations;
using cars_website_api.Migrations;

#nullable disable

namespace cars_website_api.CarsWebsite.Data.Migrations
{
    public partial class AddSearchFilterIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("caradverts", "IX_caradverts_ExpiresAt", "`ExpiresAt`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("caradverts", "IX_caradverts_BadgeExpiresAt", "`BadgeExpiresAt`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("caradverts", "IX_caradverts_GearboxId", "`GearboxId`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("caradverts", "IX_caradverts_BodyTypeId", "`BodyTypeId`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("caradverts", "IX_caradverts_Mileage", "`Mileage`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("events", "IX_events_Status", "`Status`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("events", "IX_events_StartDate", "`StartDate`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("events", "IX_events_CreatedByUserId", "`CreatedByUserId`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("payments", "IX_payments_UserId", "`UserId`"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("caradverts", "IX_caradverts_ExpiresAt"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("caradverts", "IX_caradverts_BadgeExpiresAt"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("caradverts", "IX_caradverts_GearboxId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("caradverts", "IX_caradverts_BodyTypeId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("caradverts", "IX_caradverts_Mileage"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("events", "IX_events_Status"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("events", "IX_events_StartDate"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("events", "IX_events_CreatedByUserId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("payments", "IX_payments_UserId"));
        }
    }
}
