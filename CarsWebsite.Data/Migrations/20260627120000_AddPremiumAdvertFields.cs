using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddPremiumAdvertFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` ADD COLUMN IF NOT EXISTS `RegistrationPlate` varchar(20) NULL;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` ADD COLUMN IF NOT EXISTS `HasVatInvoice` tinyint(1) NOT NULL DEFAULT 0;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` ADD COLUMN IF NOT EXISTS `IsLeasingPossible` tinyint(1) NOT NULL DEFAULT 0;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` ADD COLUMN IF NOT EXISTS `IsCreditPossible` tinyint(1) NOT NULL DEFAULT 0;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` ADD COLUMN IF NOT EXISTS `IsExchangePossible` tinyint(1) NOT NULL DEFAULT 0;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` ADD COLUMN IF NOT EXISTS `GearCount` int NULL;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` ADD COLUMN IF NOT EXISTS `MetallicPaint` tinyint(1) NOT NULL DEFAULT 0;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` ADD COLUMN IF NOT EXISTS `MaxTrailerWeight` int NULL;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` ADD COLUMN IF NOT EXISTS `IsFirstOwner` tinyint(1) NOT NULL DEFAULT 0;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` ADD COLUMN IF NOT EXISTS `IsServicedAtASO` tinyint(1) NOT NULL DEFAULT 0;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` ADD COLUMN IF NOT EXISTS `IsGaraged` tinyint(1) NOT NULL DEFAULT 0;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` ADD COLUMN IF NOT EXISTS `KeyCount` int NULL;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` ADD COLUMN IF NOT EXISTS `InsuranceUntil` datetime(6) NULL;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` ADD COLUMN IF NOT EXISTS `YoutubeUrl` varchar(500) NULL;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` DROP COLUMN IF EXISTS `RegistrationPlate`;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` DROP COLUMN IF EXISTS `HasVatInvoice`;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` DROP COLUMN IF EXISTS `IsLeasingPossible`;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` DROP COLUMN IF EXISTS `IsCreditPossible`;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` DROP COLUMN IF EXISTS `IsExchangePossible`;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` DROP COLUMN IF EXISTS `GearCount`;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` DROP COLUMN IF EXISTS `MetallicPaint`;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` DROP COLUMN IF EXISTS `MaxTrailerWeight`;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` DROP COLUMN IF EXISTS `IsFirstOwner`;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` DROP COLUMN IF EXISTS `IsServicedAtASO`;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` DROP COLUMN IF EXISTS `IsGaraged`;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` DROP COLUMN IF EXISTS `KeyCount`;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` DROP COLUMN IF EXISTS `InsuranceUntil`;");
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` DROP COLUMN IF EXISTS `YoutubeUrl`;");
        }
    }
}
