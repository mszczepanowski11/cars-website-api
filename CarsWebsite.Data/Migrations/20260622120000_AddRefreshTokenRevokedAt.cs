using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddRefreshTokenRevokedAt : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RevokedAt",
                table: "refreshtokens",
                type: "datetime(6)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RevokedAt",
                table: "refreshtokens");
        }
    }
}
