using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    /// <inheritdoc />
    public partial class AddFuelConsumptionToEngineVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FuelConsumptionCity",
                table: "engineversions",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FuelConsumptionHighway",
                table: "engineversions",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FuelConsumptionCombined",
                table: "engineversions",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "FuelConsumptionCity", table: "engineversions");
            migrationBuilder.DropColumn(name: "FuelConsumptionHighway", table: "engineversions");
            migrationBuilder.DropColumn(name: "FuelConsumptionCombined", table: "engineversions");
        }
    }
}
