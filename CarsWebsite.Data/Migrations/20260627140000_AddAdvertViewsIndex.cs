using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddAdvertViewsIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS `IX_AdvertViews_AdvertId_IpAddress_ViewedAt`
                ON `AdvertViews` (`AdvertId`, `IpAddress`(45), `ViewedAt`);
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_AdvertViews_AdvertId_IpAddress_ViewedAt` ON `AdvertViews`;");
        }
    }
}
