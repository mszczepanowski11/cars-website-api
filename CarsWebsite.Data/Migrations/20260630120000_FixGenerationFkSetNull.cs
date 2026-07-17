using Microsoft.EntityFrameworkCore.Migrations;
using cars_website_api.Migrations;

#nullable disable

namespace cars_website_api.CarsWebsite.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixGenerationFkSetNull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The original FK was created without ON DELETE, causing MySQL to default to RESTRICT.
            // This blocks the seeder from cleaning up orphan placeholder generations (e.g. "Generation I")
            // when any advert references them. Change to SET NULL so deletions succeed cleanly.
            // Guarded (not Fluent Drop/AddForeignKey): the FK may already be in the target state in
            // production, where schema drift used to be patched by startup ALTER blocks while
            // migrations silently failed - a plain DROP/ADD would abort the pending-migration chain.
            migrationBuilder.Sql(MySqlGuard.DropForeignKeyIfExists("caradverts", "FK_CarAdverts_Generations_GenerationId"));
            migrationBuilder.Sql(MySqlGuard.AddForeignKeyIfMissing("caradverts", "FK_CarAdverts_Generations_GenerationId",
                "FOREIGN KEY (`GenerationId`) REFERENCES `generations` (`Id`) ON DELETE SET NULL"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropForeignKeyIfExists("caradverts", "FK_CarAdverts_Generations_GenerationId"));
            migrationBuilder.Sql(MySqlGuard.AddForeignKeyIfMissing("caradverts", "FK_CarAdverts_Generations_GenerationId",
                "FOREIGN KEY (`GenerationId`) REFERENCES `generations` (`Id`)"));
        }
    }
}
