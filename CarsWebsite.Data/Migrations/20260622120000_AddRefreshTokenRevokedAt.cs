using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddRefreshTokenRevokedAt : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use raw SQL with IF NOT EXISTS — migrationBuilder.AddColumn() is not idempotent
            // and fails with "Duplicate column name" if the column already exists (Program.cs guard
            // or a previous partial run may have added it).
            migrationBuilder.Sql("ALTER TABLE `refreshtokens` ADD COLUMN IF NOT EXISTS `RevokedAt` datetime(6) NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE `refreshtokens` DROP COLUMN IF EXISTS `RevokedAt`");
        }
    }
}
