using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddMissingIndexes2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // payments — ImojeOrderId lookup (webhook processing, idempotency)
            migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS `UX_Payments_ImojeOrderId` ON `payments` (`ImojeOrderId`(255))");

            // adverts — expiry-based queries (expiry reminder job, expired advert cleanup)
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_Adverts_ExpiresAt` ON `adverts` (`ExpiresAt`)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS `UX_Payments_ImojeOrderId` ON `payments`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_Adverts_ExpiresAt` ON `adverts`");
        }
    }
}
