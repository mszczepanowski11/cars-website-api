using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddConversationPinArchive : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("Conversations", "IsPinned", "tinyint(1) NOT NULL DEFAULT 0"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("Conversations", "IsArchived", "tinyint(1) NOT NULL DEFAULT 0"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("Conversations", "IsPinned"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("Conversations", "IsArchived"));
        }
    }
}
