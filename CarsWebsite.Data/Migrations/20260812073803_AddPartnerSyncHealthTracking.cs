using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.CarsWebsite.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPartnerSyncHealthTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveSyncFailures",
                table: "partners",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncAttemptAt",
                table: "partners",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSyncError",
                table: "partners",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConsecutiveSyncFailures",
                table: "partners");

            migrationBuilder.DropColumn(
                name: "LastSyncAttemptAt",
                table: "partners");

            migrationBuilder.DropColumn(
                name: "LastSyncError",
                table: "partners");
        }
    }
}
