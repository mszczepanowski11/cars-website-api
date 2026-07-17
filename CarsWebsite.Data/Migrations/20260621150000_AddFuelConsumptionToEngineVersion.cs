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
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("engineversions", "FuelConsumptionCity", "decimal(5,2) NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("engineversions", "FuelConsumptionHighway", "decimal(5,2) NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("engineversions", "FuelConsumptionCombined", "decimal(5,2) NULL"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("engineversions", "FuelConsumptionCity"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("engineversions", "FuelConsumptionHighway"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("engineversions", "FuelConsumptionCombined"));
        }
    }
}
