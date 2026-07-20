using Microsoft.EntityFrameworkCore.Migrations;
using cars_website_api.Migrations;

#nullable disable

namespace cars_website_api.CarsWebsite.Data.Migrations
{
    /// <summary>
    /// Multi-language foundation for the Business Directory (blueprint: i18n per field). Adds a base
    /// Description plus an I18n JSON column holding per-language translations of name/description.
    /// Guarded (MySqlGuard) because Program.cs also adds these via startup ALTER - belt and braces.
    /// </summary>
    public partial class AddDirectoryI18n : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("directorycompanies", "Description", "varchar(2000) NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("directorycompanies", "I18n", "longtext NULL"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("directorycompanies", "I18n"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("directorycompanies", "Description"));
        }
    }
}
