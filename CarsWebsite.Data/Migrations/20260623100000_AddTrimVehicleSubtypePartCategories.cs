using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddTrimVehicleSubtypePartCategories : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Trims table
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS `trims` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `GenerationId` int NOT NULL,
                    `Name` varchar(100) NOT NULL,
                    `Description` varchar(500) NULL,
                    PRIMARY KEY (`Id`),
                    KEY `IX_trims_GenerationId` (`GenerationId`),
                    CONSTRAINT `FK_trims_generations_GenerationId` FOREIGN KEY (`GenerationId`) REFERENCES `generations` (`Id`) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");

            // VehicleSubtypes table
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS `vehiclesubtypes` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `VehicleCategoryId` int NOT NULL,
                    `Name` varchar(100) NOT NULL,
                    `NamePl` varchar(100) NULL,
                    `SortOrder` int NOT NULL DEFAULT 0,
                    PRIMARY KEY (`Id`),
                    KEY `IX_vehiclesubtypes_VehicleCategoryId` (`VehicleCategoryId`),
                    CONSTRAINT `FK_vehiclesubtypes_vehiclecategories_VehicleCategoryId` FOREIGN KEY (`VehicleCategoryId`) REFERENCES `vehiclecategories` (`Id`) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");

            // PartCategories table
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS `partcategories` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `Name` varchar(100) NOT NULL,
                    `NamePl` varchar(100) NULL,
                    `SortOrder` int NOT NULL DEFAULT 0,
                    PRIMARY KEY (`Id`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");

            // PartSubcategories table
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS `partsubcategories` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `PartCategoryId` int NOT NULL,
                    `Name` varchar(100) NOT NULL,
                    `NamePl` varchar(100) NULL,
                    `SortOrder` int NOT NULL DEFAULT 0,
                    PRIMARY KEY (`Id`),
                    KEY `IX_partsubcategories_PartCategoryId` (`PartCategoryId`),
                    CONSTRAINT `FK_partsubcategories_partcategories_PartCategoryId` FOREIGN KEY (`PartCategoryId`) REFERENCES `partcategories` (`Id`) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");

            // Add new columns to engineversions
            migrationBuilder.Sql("ALTER TABLE `engineversions` ADD COLUMN IF NOT EXISTS `TrimId` int NULL");
            migrationBuilder.Sql("ALTER TABLE `engineversions` ADD COLUMN IF NOT EXISTS `TorqueNm` int NULL");
            migrationBuilder.Sql("ALTER TABLE `engineversions` ADD COLUMN IF NOT EXISTS `Co2EmissionGkm` int NULL");
            migrationBuilder.Sql("ALTER TABLE `engineversions` ADD COLUMN IF NOT EXISTS `EuroNorm` varchar(20) NULL");
            migrationBuilder.Sql("ALTER TABLE `engineversions` ADD COLUMN IF NOT EXISTS `AvgConsumptionL` decimal(4,1) NULL");
            migrationBuilder.Sql("ALTER TABLE `engineversions` ADD COLUMN IF NOT EXISTS `Acceleration0100` decimal(4,1) NULL");
            migrationBuilder.Sql("ALTER TABLE `engineversions` ADD COLUMN IF NOT EXISTS `TopSpeedKmh` int NULL");
            migrationBuilder.Sql("ALTER TABLE `engineversions` ADD COLUMN IF NOT EXISTS `DriveType` varchar(10) NULL");
            migrationBuilder.Sql("ALTER TABLE `engineversions` ADD COLUMN IF NOT EXISTS `GearboxType` varchar(20) NULL");
            migrationBuilder.Sql("ALTER TABLE `engineversions` ADD COLUMN IF NOT EXISTS `Cylinders` int NULL");

            // FK for engineversions.TrimId is added idempotently in Program.cs startup
            // guards (try/catch), because MySQL 8.0 does not support ADD CONSTRAINT IF NOT EXISTS.

            // Add new columns to caradverts
            migrationBuilder.Sql("ALTER TABLE `caradverts` ADD COLUMN IF NOT EXISTS `TrimId` int NULL");
            migrationBuilder.Sql("ALTER TABLE `caradverts` ADD COLUMN IF NOT EXISTS `VehicleSubtypeId` int NULL");
            migrationBuilder.Sql("ALTER TABLE `caradverts` ADD COLUMN IF NOT EXISTS `PartCategoryId` int NULL");
            migrationBuilder.Sql("ALTER TABLE `caradverts` ADD COLUMN IF NOT EXISTS `PartSubcategoryId` int NULL");
            migrationBuilder.Sql("ALTER TABLE `caradverts` ADD COLUMN IF NOT EXISTS `OemNumber` varchar(100) NULL");
            migrationBuilder.Sql("ALTER TABLE `caradverts` ADD COLUMN IF NOT EXISTS `ManufacturerPartNumber` varchar(100) NULL");
            migrationBuilder.Sql("ALTER TABLE `caradverts` ADD COLUMN IF NOT EXISTS `PartManufacturer` varchar(100) NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE `caradverts` DROP COLUMN IF EXISTS `PartManufacturer`");
            migrationBuilder.Sql("ALTER TABLE `caradverts` DROP COLUMN IF EXISTS `ManufacturerPartNumber`");
            migrationBuilder.Sql("ALTER TABLE `caradverts` DROP COLUMN IF EXISTS `OemNumber`");
            migrationBuilder.Sql("ALTER TABLE `caradverts` DROP COLUMN IF EXISTS `PartSubcategoryId`");
            migrationBuilder.Sql("ALTER TABLE `caradverts` DROP COLUMN IF EXISTS `PartCategoryId`");
            migrationBuilder.Sql("ALTER TABLE `caradverts` DROP COLUMN IF EXISTS `VehicleSubtypeId`");
            migrationBuilder.Sql("ALTER TABLE `caradverts` DROP COLUMN IF EXISTS `TrimId`");
            migrationBuilder.Sql("ALTER TABLE `engineversions` DROP FOREIGN KEY IF EXISTS `FK_engineversions_trims_TrimId`");
            migrationBuilder.Sql("ALTER TABLE `engineversions` DROP COLUMN IF EXISTS `Cylinders`");
            migrationBuilder.Sql("ALTER TABLE `engineversions` DROP COLUMN IF EXISTS `GearboxType`");
            migrationBuilder.Sql("ALTER TABLE `engineversions` DROP COLUMN IF EXISTS `DriveType`");
            migrationBuilder.Sql("ALTER TABLE `engineversions` DROP COLUMN IF EXISTS `TopSpeedKmh`");
            migrationBuilder.Sql("ALTER TABLE `engineversions` DROP COLUMN IF EXISTS `Acceleration0100`");
            migrationBuilder.Sql("ALTER TABLE `engineversions` DROP COLUMN IF EXISTS `AvgConsumptionL`");
            migrationBuilder.Sql("ALTER TABLE `engineversions` DROP COLUMN IF EXISTS `EuroNorm`");
            migrationBuilder.Sql("ALTER TABLE `engineversions` DROP COLUMN IF EXISTS `Co2EmissionGkm`");
            migrationBuilder.Sql("ALTER TABLE `engineversions` DROP COLUMN IF EXISTS `TorqueNm`");
            migrationBuilder.Sql("ALTER TABLE `engineversions` DROP COLUMN IF EXISTS `TrimId`");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `partsubcategories`");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `partcategories`");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `vehiclesubtypes`");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `trims`");
        }
    }
}
