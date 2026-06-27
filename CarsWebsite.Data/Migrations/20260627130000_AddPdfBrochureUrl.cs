using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddPdfBrochureUrl : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` ADD COLUMN IF NOT EXISTS `PdfBrochureUrl` varchar(1000) NULL;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE `CarAdverts` DROP COLUMN IF EXISTS `PdfBrochureUrl`;");
        }
    }
}
