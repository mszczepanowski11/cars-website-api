using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddMissingIndexes2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // payments — ImojeOrderId lookup (webhook processing, idempotency)
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("payments", "UX_Payments_ImojeOrderId", "`ImojeOrderId`(255)", "UNIQUE"));

            // adverts — expiry-based queries (expiry reminder job, expired advert cleanup)
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("adverts", "IX_Adverts_ExpiresAt", "`ExpiresAt`"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("payments", "UX_Payments_ImojeOrderId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("adverts", "IX_Adverts_ExpiresAt"));
        }
    }
}
