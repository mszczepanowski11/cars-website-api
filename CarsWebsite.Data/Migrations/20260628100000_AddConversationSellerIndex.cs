using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddConversationSellerIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("Conversations", "IX_Conversations_SellerId_LastMessageAt", "`SellerId`, `LastMessageAt` DESC"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("Conversations", "IX_Conversations_SellerId_LastMessageAt"));
        }
    }
}
