using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    /// <inheritdoc />
    public partial class AddBrandModelToFeatureCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use IF NOT EXISTS so this is safe to run on a DB where EnsureCreated
            // already created the columns from the updated entity model.
            migrationBuilder.Sql(@"
                ALTER TABLE `FeatureCategories`
                    ADD COLUMN IF NOT EXISTS `BrandId` int NULL,
                    ADD COLUMN IF NOT EXISTS `ModelId` int NULL;
            ");

            // FK constraints added idempotently in Program.cs startup guards (try/catch),
            // because MySQL 8.0 does not support ADD CONSTRAINT IF NOT EXISTS.

            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_FeatureCategories_BrandId` ON `FeatureCategories` (`BrandId`)");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_FeatureCategories_ModelId` ON `FeatureCategories` (`ModelId`)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE `FeatureCategories`
                    DROP FOREIGN KEY IF EXISTS `FK_FeatureCategories_Brands_BrandId`,
                    DROP FOREIGN KEY IF EXISTS `FK_FeatureCategories_Models_ModelId`,
                    DROP INDEX IF EXISTS `IX_FeatureCategories_BrandId`,
                    DROP INDEX IF EXISTS `IX_FeatureCategories_ModelId`,
                    DROP COLUMN IF EXISTS `BrandId`,
                    DROP COLUMN IF EXISTS `ModelId`;
            ");
        }
    }
}
