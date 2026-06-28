using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.CarsWebsite.Data.Migrations
{
    public partial class AddSearchFilterIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_caradverts_ExpiresAt` ON `caradverts` (`ExpiresAt`);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_caradverts_BadgeExpiresAt` ON `caradverts` (`BadgeExpiresAt`);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_caradverts_GearboxId` ON `caradverts` (`GearboxId`);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_caradverts_BodyTypeId` ON `caradverts` (`BodyTypeId`);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_caradverts_Mileage` ON `caradverts` (`Mileage`);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_events_Status` ON `events` (`Status`);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_events_StartDate` ON `events` (`StartDate`);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_events_CreatedByUserId` ON `events` (`CreatedByUserId`);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_payments_UserId` ON `payments` (`UserId`);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_caradverts_ExpiresAt` ON `caradverts`;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_caradverts_BadgeExpiresAt` ON `caradverts`;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_caradverts_GearboxId` ON `caradverts`;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_caradverts_BodyTypeId` ON `caradverts`;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_caradverts_Mileage` ON `caradverts`;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_events_Status` ON `events`;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_events_StartDate` ON `events`;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_events_CreatedByUserId` ON `events`;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_payments_UserId` ON `payments`;");
        }
    }
}
