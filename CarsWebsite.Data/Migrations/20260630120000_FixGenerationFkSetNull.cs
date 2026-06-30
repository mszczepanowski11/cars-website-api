using Microsoft.EntityFrameworkCore.Migrations;

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
            migrationBuilder.DropForeignKey(
                name: "FK_CarAdverts_Generations_GenerationId",
                table: "CarAdverts");

            migrationBuilder.AddForeignKey(
                name: "FK_CarAdverts_Generations_GenerationId",
                table: "CarAdverts",
                column: "GenerationId",
                principalTable: "Generations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CarAdverts_Generations_GenerationId",
                table: "CarAdverts");

            migrationBuilder.AddForeignKey(
                name: "FK_CarAdverts_Generations_GenerationId",
                table: "CarAdverts",
                column: "GenerationId",
                principalTable: "Generations",
                principalColumn: "Id");
        }
    }
}
