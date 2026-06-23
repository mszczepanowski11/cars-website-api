using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.CarsWebsite.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixMissingForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ─── 1. AdvertViews: add FK to caradverts with CASCADE DELETE ─────────
            // Previously AdvertViews had no FK constraint — orphaned rows accumulated on advert delete.
            migrationBuilder.Sql(@"
                ALTER TABLE `advertviews`
                ADD CONSTRAINT `FK_advertviews_caradverts_AdvertId`
                FOREIGN KEY (`AdvertId`) REFERENCES `caradverts`(`Id`) ON DELETE CASCADE;
            ");

            // ─── 2. caradverts: add FK constraints for nullable FK columns ────────
            // Migration 20260623100000 added these columns via raw SQL with no FK constraints.
            // EF Core OnDelete(SetNull) config exists in C# but had no DB-level enforcement.

            migrationBuilder.Sql(@"
                ALTER TABLE `caradverts`
                ADD CONSTRAINT `FK_caradverts_trims_TrimId`
                FOREIGN KEY (`TrimId`) REFERENCES `trims`(`Id`) ON DELETE SET NULL;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE `caradverts`
                ADD CONSTRAINT `FK_caradverts_vehiclesubtypes_VehicleSubtypeId`
                FOREIGN KEY (`VehicleSubtypeId`) REFERENCES `vehiclesubtypes`(`Id`) ON DELETE SET NULL;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE `caradverts`
                ADD CONSTRAINT `FK_caradverts_partcategories_PartCategoryId`
                FOREIGN KEY (`PartCategoryId`) REFERENCES `partcategories`(`Id`) ON DELETE SET NULL;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE `caradverts`
                ADD CONSTRAINT `FK_caradverts_partsubcategories_PartSubcategoryId`
                FOREIGN KEY (`PartSubcategoryId`) REFERENCES `partsubcategories`(`Id`) ON DELETE SET NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE `advertviews` DROP FOREIGN KEY `FK_advertviews_caradverts_AdvertId`;");
            migrationBuilder.Sql("ALTER TABLE `caradverts` DROP FOREIGN KEY `FK_caradverts_trims_TrimId`;");
            migrationBuilder.Sql("ALTER TABLE `caradverts` DROP FOREIGN KEY `FK_caradverts_vehiclesubtypes_VehicleSubtypeId`;");
            migrationBuilder.Sql("ALTER TABLE `caradverts` DROP FOREIGN KEY `FK_caradverts_partcategories_PartCategoryId`;");
            migrationBuilder.Sql("ALTER TABLE `caradverts` DROP FOREIGN KEY `FK_caradverts_partsubcategories_PartSubcategoryId`;");
        }
    }
}
