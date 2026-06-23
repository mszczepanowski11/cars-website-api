using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.CarsWebsite.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixMissingForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MySQL 8.0 does not support ADD CONSTRAINT IF NOT EXISTS, so the original SQL
            // would fail with "Duplicate foreign key constraint name" if the constraints
            // already existed. FK constraints are now added idempotently in Program.cs
            // startup guards (try/catch), so this migration is intentionally a no-op.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
