using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddAdvertViewsIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("AdvertViews", "IX_AdvertViews_AdvertId_IpAddress_ViewedAt", "`AdvertId`, `IpAddress`(45), `ViewedAt`"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("AdvertViews", "IX_AdvertViews_AdvertId_IpAddress_ViewedAt"));
        }
    }
}
