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
            // Use raw SQL with IF NOT EXISTS — migrationBuilder.AddColumn() is not idempotent
            // and fails with "Duplicate column name" if EnsureCreated already created the columns.
            migrationBuilder.Sql("ALTER TABLE `engineversions` ADD COLUMN IF NOT EXISTS `FuelConsumptionCity` decimal(5,2) NULL");
            migrationBuilder.Sql("ALTER TABLE `engineversions` ADD COLUMN IF NOT EXISTS `FuelConsumptionHighway` decimal(5,2) NULL");
            migrationBuilder.Sql("ALTER TABLE `engineversions` ADD COLUMN IF NOT EXISTS `FuelConsumptionCombined` decimal(5,2) NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE `engineversions` DROP COLUMN IF EXISTS `FuelConsumptionCity`");
            migrationBuilder.Sql("ALTER TABLE `engineversions` DROP COLUMN IF EXISTS `FuelConsumptionHighway`");
            migrationBuilder.Sql("ALTER TABLE `engineversions` DROP COLUMN IF EXISTS `FuelConsumptionCombined`");
        }
    }
}
