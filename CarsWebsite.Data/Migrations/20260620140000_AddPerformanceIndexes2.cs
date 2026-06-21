using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddPerformanceIndexes2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(name: "IX_CarAdverts_Price", table: "caradverts", column: "Price");
            migrationBuilder.CreateIndex(name: "IX_CarAdverts_Year", table: "caradverts", column: "Year");
            migrationBuilder.CreateIndex(name: "IX_CarAdverts_CreatedAt", table: "caradverts", column: "CreatedAt");
            migrationBuilder.CreateIndex(name: "IX_CarAdverts_VehicleCategoryId", table: "caradverts", column: "VehicleCategoryId");
            migrationBuilder.CreateIndex(name: "IX_Users_GoogleId", table: "users", column: "GoogleId");
            migrationBuilder.CreateIndex(name: "IX_Users_FacebookId", table: "users", column: "FacebookId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_CarAdverts_Price", table: "caradverts");
            migrationBuilder.DropIndex(name: "IX_CarAdverts_Year", table: "caradverts");
            migrationBuilder.DropIndex(name: "IX_CarAdverts_CreatedAt", table: "caradverts");
            migrationBuilder.DropIndex(name: "IX_CarAdverts_VehicleCategoryId", table: "caradverts");
            migrationBuilder.DropIndex(name: "IX_Users_GoogleId", table: "users");
            migrationBuilder.DropIndex(name: "IX_Users_FacebookId", table: "users");
        }
    }
}
