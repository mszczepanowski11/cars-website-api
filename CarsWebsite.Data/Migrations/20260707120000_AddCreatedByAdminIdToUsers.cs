using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatedByAdminIdToUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guarded (not Fluent AddColumn): the column may already exist in production, where
            // schema drift used to be patched by startup ALTER blocks while migrations silently
            // failed - a plain ADD COLUMN would abort the whole pending-migration chain here.
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("users", "CreatedByAdminId", "int NULL"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("users", "CreatedByAdminId"));
        }
    }
}
