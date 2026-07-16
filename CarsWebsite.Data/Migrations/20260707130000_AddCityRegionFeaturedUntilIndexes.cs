using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    /// <summary>
    /// Adds missing indexes for City/Region (near-universal local-marketplace search filters)
    /// and CarAdverts.FeaturedUntil (used to sort promoted listings to the top of results).
    /// </summary>
    public partial class AddCityRegionFeaturedUntilIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_Adverts_City_IsActive` ON `adverts` (`City`, `IsActive`)");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_Adverts_Region_IsActive` ON `adverts` (`Region`, `IsActive`)");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_CarAdverts_FeaturedUntil` ON `caradverts` (`FeaturedUntil`)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_Adverts_City_IsActive` ON `adverts`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_Adverts_Region_IsActive` ON `adverts`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_CarAdverts_FeaturedUntil` ON `caradverts`");
        }
    }
}
