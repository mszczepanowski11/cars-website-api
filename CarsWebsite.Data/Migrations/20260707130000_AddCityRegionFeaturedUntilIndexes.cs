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
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("adverts", "IX_Adverts_City_IsActive", "`City`, `IsActive`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("adverts", "IX_Adverts_Region_IsActive", "`Region`, `IsActive`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("caradverts", "IX_CarAdverts_FeaturedUntil", "`FeaturedUntil`"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("adverts", "IX_Adverts_City_IsActive"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("adverts", "IX_Adverts_Region_IsActive"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("caradverts", "IX_CarAdverts_FeaturedUntil"));
        }
    }
}
