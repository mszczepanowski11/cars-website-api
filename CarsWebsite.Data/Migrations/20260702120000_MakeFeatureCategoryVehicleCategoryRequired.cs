using Microsoft.EntityFrameworkCore.Migrations;
using cars_website_api.Migrations;

#nullable disable

namespace cars_website_api.CarsWebsite.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeFeatureCategoryVehicleCategoryRequired : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A FeatureCategory with VehicleCategoryId = NULL matched every vehicle category
            // (see GetFeatureCategoriesByContextAsync), which caused equipment from one category
            // (e.g. trucks) to leak onto listings in another (e.g. cars). Production applies the
            // equivalent data backfill + column change via idempotent startup guards in Program.cs
            // (this codebase does not run `dotnet ef database update` automatically — see the
            // intentionally no-op FixMissingForeignKeys migration), but this migration keeps the
            // EF model/migration history consistent for any environment that does apply migrations
            // directly.
            migrationBuilder.Sql(
                "UPDATE `featurecategories` fc JOIN `vehiclecategories` vc ON vc.`Slug` = 'inne' " +
                "SET fc.`VehicleCategoryId` = vc.`Id` WHERE fc.`VehicleCategoryId` IS NULL");

            migrationBuilder.Sql(MySqlGuard.DropForeignKeyIfExists("featurecategories", "FK_FeatureCategories_VehicleCategories_VehicleCategoryId"));

            migrationBuilder.AlterColumn<int>(
                name: "VehicleCategoryId",
                table: "FeatureCategories",
                type: "int",
                nullable: false,
                oldClrType: typeof(int?),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.Sql(MySqlGuard.AddForeignKeyIfMissing("featurecategories", "FK_FeatureCategories_VehicleCategories_VehicleCategoryId",
                "FOREIGN KEY (`VehicleCategoryId`) REFERENCES `vehiclecategories` (`Id`) ON DELETE RESTRICT"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropForeignKeyIfExists("featurecategories", "FK_FeatureCategories_VehicleCategories_VehicleCategoryId"));

            migrationBuilder.AlterColumn<int?>(
                name: "VehicleCategoryId",
                table: "FeatureCategories",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.Sql(MySqlGuard.AddForeignKeyIfMissing("featurecategories", "FK_FeatureCategories_VehicleCategories_VehicleCategoryId",
                "FOREIGN KEY (`VehicleCategoryId`) REFERENCES `vehiclecategories` (`Id`) ON DELETE SET NULL"));
        }
    }
}
