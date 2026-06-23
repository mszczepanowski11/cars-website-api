using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddVehicleCategoryIdToFeatureCategory : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE `featurecategories`
                    ADD COLUMN IF NOT EXISTS `VehicleCategoryId` int NULL;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE `featurecategories`
                    ADD CONSTRAINT IF NOT EXISTS `FK_FeatureCategories_VehicleCategories_VehicleCategoryId`
                        FOREIGN KEY (`VehicleCategoryId`) REFERENCES `vehiclecategories`(`Id`) ON DELETE SET NULL;
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS `IX_FeatureCategories_VehicleCategoryId` ON `featurecategories` (`VehicleCategoryId`);
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE `featurecategories`
                    DROP FOREIGN KEY IF EXISTS `FK_FeatureCategories_VehicleCategories_VehicleCategoryId`,
                    DROP INDEX IF EXISTS `IX_FeatureCategories_VehicleCategoryId`,
                    DROP COLUMN IF EXISTS `VehicleCategoryId`;
            ");
        }
    }
}
