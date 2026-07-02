using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddEnginePlausibilityRules : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS `BrandAllowedFuelTypes` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `BrandId` int NOT NULL,
                    `FuelTypeId` int NOT NULL,
                    PRIMARY KEY (`Id`),
                    KEY `IX_BrandAllowedFuelTypes_BrandId` (`BrandId`),
                    KEY `IX_BrandAllowedFuelTypes_FuelTypeId` (`FuelTypeId`),
                    CONSTRAINT `FK_BrandAllowedFuelTypes_Brands_BrandId` FOREIGN KEY (`BrandId`) REFERENCES `Brands` (`Id`) ON DELETE CASCADE,
                    CONSTRAINT `FK_BrandAllowedFuelTypes_FuelTypes_FuelTypeId` FOREIGN KEY (`FuelTypeId`) REFERENCES `FuelTypes` (`Id`) ON DELETE RESTRICT
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS `ModelNamePlausibilityRules` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `NamePattern` varchar(100) NOT NULL,
                    `MinPowerHP` int NOT NULL,
                    `Description` varchar(500) NULL,
                    PRIMARY KEY (`Id`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS `BrandAllowedFuelTypes`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `ModelNamePlausibilityRules`;");
        }
    }
}
