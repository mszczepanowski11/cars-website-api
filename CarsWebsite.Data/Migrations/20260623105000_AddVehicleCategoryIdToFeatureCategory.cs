using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddVehicleCategoryIdToFeatureCategory : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE `FeatureCategories`
                    ADD COLUMN IF NOT EXISTS `VehicleCategoryId` int NULL;
            ");

            // FK added idempotently in Program.cs startup guards (MySQL 8.0 does not support ADD CONSTRAINT IF NOT EXISTS).

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS `IX_FeatureCategories_VehicleCategoryId` ON `FeatureCategories` (`VehicleCategoryId`);
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE `FeatureCategories`
                    DROP FOREIGN KEY IF EXISTS `FK_FeatureCategories_VehicleCategories_VehicleCategoryId`,
                    DROP INDEX IF EXISTS `IX_FeatureCategories_VehicleCategoryId`,
                    DROP COLUMN IF EXISTS `VehicleCategoryId`;
            ");
        }
    }
}
