using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddCustomCategoryRequestResults : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("customcategoryrequests", "ResultingVehicleCategoryId", "int NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("customcategoryrequests", "ResultingVehicleSubtypeId", "int NULL"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("customcategoryrequests", "IX_customcategoryrequests_ResultingVehicleCategoryId", "`ResultingVehicleCategoryId`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("customcategoryrequests", "IX_customcategoryrequests_ResultingVehicleSubtypeId", "`ResultingVehicleSubtypeId`"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropForeignKeyIfExists("customcategoryrequests", "FK_customcategoryrequests_VehicleCategories_ResultingVehicleCategoryId"));
            migrationBuilder.Sql(MySqlGuard.DropForeignKeyIfExists("customcategoryrequests", "FK_customcategoryrequests_VehicleSubtypes_ResultingVehicleSubtypeId"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("customcategoryrequests", "ResultingVehicleCategoryId"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("customcategoryrequests", "ResultingVehicleSubtypeId"));
        }
    }
}
