using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddCustomCategoryRequestResults : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE `customcategoryrequests` ADD COLUMN IF NOT EXISTS `ResultingVehicleCategoryId` int NULL;");
            migrationBuilder.Sql("ALTER TABLE `customcategoryrequests` ADD COLUMN IF NOT EXISTS `ResultingVehicleSubtypeId` int NULL;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_customcategoryrequests_ResultingVehicleCategoryId` ON `customcategoryrequests` (`ResultingVehicleCategoryId`);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_customcategoryrequests_ResultingVehicleSubtypeId` ON `customcategoryrequests` (`ResultingVehicleSubtypeId`);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE `customcategoryrequests`
                    DROP FOREIGN KEY IF EXISTS `FK_customcategoryrequests_VehicleCategories_ResultingVehicleCategoryId`,
                    DROP FOREIGN KEY IF EXISTS `FK_customcategoryrequests_VehicleSubtypes_ResultingVehicleSubtypeId`,
                    DROP COLUMN IF EXISTS `ResultingVehicleCategoryId`,
                    DROP COLUMN IF EXISTS `ResultingVehicleSubtypeId`;
            ");
        }
    }
}
