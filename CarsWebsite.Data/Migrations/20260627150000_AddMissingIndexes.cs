using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddMissingIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("CarAdverts", "IX_CarAdverts_Vin", "`Vin`(17)"));

            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("Payments", "IX_Payments_ImojeOrderId", "`ImojeOrderId`(40)"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("CarAdverts", "IX_CarAdverts_Vin"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("Payments", "IX_Payments_ImojeOrderId"));
        }
    }
}
