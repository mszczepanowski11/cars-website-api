using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddRefreshTokenRevokedAt : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE `refreshtokens` ADD COLUMN IF NOT EXISTS `RevokedAt` datetime(6) NULL;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE `refreshtokens` DROP COLUMN IF EXISTS `RevokedAt`;");
        }
    }
}
