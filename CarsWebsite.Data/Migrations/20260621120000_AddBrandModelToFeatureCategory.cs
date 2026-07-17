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
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("featurecategories", "BrandId", "int NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("featurecategories", "ModelId", "int NULL"));

            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("featurecategories", "IX_FeatureCategories_BrandId", "`BrandId`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("featurecategories", "IX_FeatureCategories_ModelId", "`ModelId`"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropForeignKeyIfExists("featurecategories", "FK_FeatureCategories_Brands_BrandId"));
            migrationBuilder.Sql(MySqlGuard.DropForeignKeyIfExists("featurecategories", "FK_FeatureCategories_Models_ModelId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("featurecategories", "IX_FeatureCategories_BrandId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("featurecategories", "IX_FeatureCategories_ModelId"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("featurecategories", "BrandId"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("featurecategories", "ModelId"));
        }
    }
}
