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
                ALTER TABLE `featurecategories`
                    ADD COLUMN IF NOT EXISTS `BrandId` int NULL,
                    ADD COLUMN IF NOT EXISTS `ModelId` int NULL;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE `featurecategories`
                    ADD CONSTRAINT IF NOT EXISTS `FK_FeatureCategories_Brands_BrandId`
                        FOREIGN KEY (`BrandId`) REFERENCES `brands`(`Id`) ON DELETE SET NULL,
                    ADD CONSTRAINT IF NOT EXISTS `FK_FeatureCategories_Models_ModelId`
                        FOREIGN KEY (`ModelId`) REFERENCES `models`(`Id`) ON DELETE SET NULL;
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS `IX_FeatureCategories_BrandId` ON `featurecategories` (`BrandId`);
                CREATE INDEX IF NOT EXISTS `IX_FeatureCategories_ModelId` ON `featurecategories` (`ModelId`);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE `featurecategories`
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
