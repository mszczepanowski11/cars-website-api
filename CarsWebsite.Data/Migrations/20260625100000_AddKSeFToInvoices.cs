using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddKSeFToInvoices : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE `invoices` ADD COLUMN IF NOT EXISTS `KSeFReferenceNumber` varchar(200) NULL;");
            migrationBuilder.Sql(
                "ALTER TABLE `invoices` ADD COLUMN IF NOT EXISTS `IsKSeFSent` tinyint(1) NOT NULL DEFAULT 0;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE `invoices` DROP COLUMN IF EXISTS `KSeFReferenceNumber`;");
            migrationBuilder.Sql(
                "ALTER TABLE `invoices` DROP COLUMN IF EXISTS `IsKSeFSent`;");
        }
    }
}
