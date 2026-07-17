using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddVehicleCategoryIdToFeatureCategory : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("featurecategories", "VehicleCategoryId", "int NULL"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("featurecategories", "IX_FeatureCategories_VehicleCategoryId", "`VehicleCategoryId`"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropForeignKeyIfExists("featurecategories", "FK_FeatureCategories_VehicleCategories_VehicleCategoryId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("featurecategories", "IX_FeatureCategories_VehicleCategoryId"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("featurecategories", "VehicleCategoryId"));
        }
    }
}
