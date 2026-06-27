using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddMissingIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS `IX_CarAdverts_Vin`
                ON `CarAdverts` (`Vin`(17));
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS `IX_Payments_ImojeOrderId`
                ON `Payments` (`ImojeOrderId`(40));
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_CarAdverts_Vin` ON `CarAdverts`;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_Payments_ImojeOrderId` ON `Payments`;");
        }
    }
}
