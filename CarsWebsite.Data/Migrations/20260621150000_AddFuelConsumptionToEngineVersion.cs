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
            migrationBuilder.Sql(@"
                ALTER TABLE `engineversions`
                    ADD COLUMN IF NOT EXISTS `FuelConsumptionCity`     decimal(5,2) NULL,
                    ADD COLUMN IF NOT EXISTS `FuelConsumptionHighway`  decimal(5,2) NULL,
                    ADD COLUMN IF NOT EXISTS `FuelConsumptionCombined` decimal(5,2) NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE `engineversions`
                    DROP COLUMN IF EXISTS `FuelConsumptionCity`,
                    DROP COLUMN IF EXISTS `FuelConsumptionHighway`,
                    DROP COLUMN IF EXISTS `FuelConsumptionCombined`;
            ");
        }
    }
}
