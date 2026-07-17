using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddVehicleSubtypeSlugAndSubtypeFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("vehiclesubtypes", "Slug", "varchar(100) NULL"));

            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("caradverts", "OperatingWeightKg", "int NULL, ADD COLUMN IF NOT EXISTS `WorkingWidthCm` int NULL, ADD COLUMN IF NOT EXISTS `MaxDiggingDepthM` decimal(5,2) NULL, ADD COLUMN IF NOT EXISTS `BucketCapacityL` int NULL, ADD COLUMN IF NOT EXISTS `TankCapacityL` int NULL"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("vehiclesubtypes", "Slug"));

            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("caradverts", "OperatingWeightKg"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("caradverts", "WorkingWidthCm"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("caradverts", "MaxDiggingDepthM"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("caradverts", "BucketCapacityL"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("caradverts", "TankCapacityL"));
        }
    }
}
