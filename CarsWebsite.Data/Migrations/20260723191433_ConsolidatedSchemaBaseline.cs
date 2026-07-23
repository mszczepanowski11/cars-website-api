using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    /// <inheritdoc />
    // Deliberately empty Up/Down. 41 of the 47 migrations that predate this one were added by hand
    // over ~4 months without ever running `dotnet ef migrations add` (no matching .Designer.cs), so
    // EF's migration system was never actually aware of them - `__EFMigrationsHistory` only ever
    // recorded the original 6 (InitialCatalog .. UpdatedFeatureEntity, March/April). Every column,
    // index, and constraint added since then reached real databases only via Program.cs's ad-hoc
    // startup guards or via EnsureCreated() on brand-new databases, never via `Database.Migrate()`.
    //
    // Scaffolding this migration normally (`dotnet ef migrations add`) reflects that: it diffs the
    // live entity model against the stale April snapshot and produces ~600 operations, including
    // ~370 drops - not real intended drops, just artifacts of 4 months of hand-edited entity classes
    // the snapshot was never told about. Actually running that generated body against any of today's
    // databases (which already have the real, current schema - confirmed via guards) would be
    // destructive for no reason.
    //
    // So this migration carries no DDL. Its only job is to become the new, EF-recognized checkpoint:
    // once applied (a genuine no-op), AppDbContextModelSnapshot.cs correctly reflects today's actual
    // model, so every future `dotnet ef migrations add` produces a small, accurate diff again instead
    // of silently drifting the way the last 41 did.
    public partial class ConsolidatedSchemaBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
