using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddPartCatalogFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` ADD COLUMN IF NOT EXISTS `Side` varchar(20) NULL;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` ADD COLUMN IF NOT EXISTS `Quantity` int NULL;");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS `PartCompatibilities` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `CarAdvertId` int NOT NULL,
                    `BrandId` int NOT NULL,
                    `ModelId` int NULL,
                    `GenerationId` int NULL,
                    PRIMARY KEY (`Id`),
                    KEY `IX_PartCompatibilities_CarAdvertId` (`CarAdvertId`),
                    KEY `IX_PartCompatibilities_BrandId` (`BrandId`),
                    KEY `IX_PartCompatibilities_ModelId` (`ModelId`),
                    KEY `IX_PartCompatibilities_GenerationId` (`GenerationId`),
                    CONSTRAINT `FK_PartCompatibilities_CarAdverts_CarAdvertId` FOREIGN KEY (`CarAdvertId`) REFERENCES `CarAdverts` (`Id`) ON DELETE CASCADE,
                    CONSTRAINT `FK_PartCompatibilities_Brands_BrandId` FOREIGN KEY (`BrandId`) REFERENCES `Brands` (`Id`) ON DELETE RESTRICT,
                    CONSTRAINT `FK_PartCompatibilities_Models_ModelId` FOREIGN KEY (`ModelId`) REFERENCES `Models` (`Id`) ON DELETE RESTRICT,
                    CONSTRAINT `FK_PartCompatibilities_Generations_GenerationId` FOREIGN KEY (`GenerationId`) REFERENCES `Generations` (`Id`) ON DELETE RESTRICT
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS `PartCompatibilities`;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` DROP COLUMN IF EXISTS `Quantity`;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` DROP COLUMN IF EXISTS `Side`;");
        }
    }
}
