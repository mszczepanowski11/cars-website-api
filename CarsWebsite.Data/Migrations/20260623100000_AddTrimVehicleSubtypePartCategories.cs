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
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("engineversions", "TrimId", "int NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("engineversions", "TorqueNm", "int NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("engineversions", "Co2EmissionGkm", "int NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("engineversions", "EuroNorm", "varchar(20) NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("engineversions", "AvgConsumptionL", "decimal(4,1) NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("engineversions", "Acceleration0100", "decimal(4,1) NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("engineversions", "TopSpeedKmh", "int NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("engineversions", "DriveType", "varchar(10) NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("engineversions", "GearboxType", "varchar(20) NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("engineversions", "Cylinders", "int NULL"));

            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("engineversions", "IX_engineversions_TrimId", "`TrimId`"));

            // Add new columns to caradverts
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("caradverts", "TrimId", "int NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("caradverts", "VehicleSubtypeId", "int NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("caradverts", "PartCategoryId", "int NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("caradverts", "PartSubcategoryId", "int NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("caradverts", "OemNumber", "varchar(100) NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("caradverts", "ManufacturerPartNumber", "varchar(100) NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("caradverts", "PartManufacturer", "varchar(100) NULL"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("caradverts", "PartManufacturer"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("caradverts", "ManufacturerPartNumber"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("caradverts", "OemNumber"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("caradverts", "PartSubcategoryId"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("caradverts", "PartCategoryId"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("caradverts", "VehicleSubtypeId"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("caradverts", "TrimId"));
            migrationBuilder.Sql(MySqlGuard.DropForeignKeyIfExists("engineversions", "FK_engineversions_trims_TrimId"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("engineversions", "Cylinders"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("engineversions", "GearboxType"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("engineversions", "DriveType"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("engineversions", "TopSpeedKmh"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("engineversions", "Acceleration0100"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("engineversions", "AvgConsumptionL"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("engineversions", "EuroNorm"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("engineversions", "Co2EmissionGkm"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("engineversions", "TorqueNm"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("engineversions", "TrimId"));
            migrationBuilder.Sql("DROP TABLE IF EXISTS `partsubcategories`");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `partcategories`");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `vehiclesubtypes`");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `trims`");
        }
    }
}
