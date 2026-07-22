using Microsoft.EntityFrameworkCore.Migrations;
using cars_website_api.Migrations;

#nullable disable

namespace cars_website_api.CarsWebsite.Data.Migrations
{
    /// <summary>
    /// "Inteligentny formularz": scopes AttributeDefinition down to Brand/Model/Generation/Trim so the
    /// add-advert form can surface vehicle-specific fields (BMW → xDrive/Head-Up Display, Golf GTI →
    /// DSG/DCC). Each column is nullable = "any at that level". Guarded (MySqlGuard) + Program.cs
    /// startup ALTER as belt-and-braces.
    /// </summary>
    public partial class AddVehicleScopingToAttributes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("attributedefinitions", "BrandId", "int NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("attributedefinitions", "ModelId", "int NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("attributedefinitions", "GenerationId", "int NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("attributedefinitions", "TrimId", "int NULL"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("attributedefinitions",
                "IX_attributedefinitions_scope", "`VehicleCategoryId`, `BrandId`, `ModelId`, `GenerationId`, `TrimId`"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("attributedefinitions", "IX_attributedefinitions_scope"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("attributedefinitions", "TrimId"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("attributedefinitions", "GenerationId"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("attributedefinitions", "ModelId"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("attributedefinitions", "BrandId"));
        }
    }
}
