using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.CarsWebsite.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCarAdvertDuplicateDetection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DuplicateMatchReason",
                table: "caradverts",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "DuplicateOfId",
                table: "caradverts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_caradverts_DuplicateOfId",
                table: "caradverts",
                column: "DuplicateOfId");

            migrationBuilder.AddForeignKey(
                name: "FK_caradverts_caradverts_DuplicateOfId",
                table: "caradverts",
                column: "DuplicateOfId",
                principalTable: "caradverts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_caradverts_caradverts_DuplicateOfId",
                table: "caradverts");

            migrationBuilder.DropIndex(
                name: "IX_caradverts_DuplicateOfId",
                table: "caradverts");

            migrationBuilder.DropColumn(
                name: "DuplicateMatchReason",
                table: "caradverts");

            migrationBuilder.DropColumn(
                name: "DuplicateOfId",
                table: "caradverts");
        }
    }
}
