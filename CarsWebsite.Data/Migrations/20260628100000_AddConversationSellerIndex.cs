using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddConversationSellerIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS `IX_Conversations_SellerId_LastMessageAt`
                ON `Conversations` (`SellerId`, `LastMessageAt` DESC);
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_Conversations_SellerId_LastMessageAt` ON `Conversations`;");
        }
    }
}
