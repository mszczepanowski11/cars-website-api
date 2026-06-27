using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddConversationPinArchive : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE `Conversations` ADD COLUMN IF NOT EXISTS `IsPinned` tinyint(1) NOT NULL DEFAULT 0;");
            migrationBuilder.Sql(
                "ALTER TABLE `Conversations` ADD COLUMN IF NOT EXISTS `IsArchived` tinyint(1) NOT NULL DEFAULT 0;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE `Conversations` DROP COLUMN IF EXISTS `IsPinned`;");
            migrationBuilder.Sql(
                "ALTER TABLE `Conversations` DROP COLUMN IF EXISTS `IsArchived`;");
        }
    }
}
