using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddVehicleSubtypeSlugAndSubtypeFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE `vehiclesubtypes` ADD COLUMN IF NOT EXISTS `Slug` varchar(100) NULL;");

            migrationBuilder.Sql(@"
                ALTER TABLE `caradverts`
                    ADD COLUMN IF NOT EXISTS `OperatingWeightKg`  int NULL,
                    ADD COLUMN IF NOT EXISTS `WorkingWidthCm`     int NULL,
                    ADD COLUMN IF NOT EXISTS `MaxDiggingDepthM`   decimal(5,2) NULL,
                    ADD COLUMN IF NOT EXISTS `BucketCapacityL`    int NULL,
                    ADD COLUMN IF NOT EXISTS `TankCapacityL`      int NULL;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE `vehiclesubtypes` DROP COLUMN IF EXISTS `Slug`;");

            migrationBuilder.Sql(@"
                ALTER TABLE `caradverts`
                    DROP COLUMN IF EXISTS `OperatingWeightKg`,
                    DROP COLUMN IF EXISTS `WorkingWidthCm`,
                    DROP COLUMN IF EXISTS `MaxDiggingDepthM`,
                    DROP COLUMN IF EXISTS `BucketCapacityL`,
                    DROP COLUMN IF EXISTS `TankCapacityL`;
            ");
        }
    }
}
