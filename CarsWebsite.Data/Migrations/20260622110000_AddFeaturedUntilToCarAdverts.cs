using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    /// <summary>
    /// B-25: Add FeaturedUntil column to caradverts table, mirroring the
    /// Event.FeaturedUntil pattern for consistent featured-promotion tracking.
    /// </summary>
    public partial class AddFeaturedUntilToCarAdverts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE `caradverts` ADD COLUMN IF NOT EXISTS `FeaturedUntil` datetime(6) NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE `caradverts` DROP COLUMN IF EXISTS `FeaturedUntil`");
        }
    }
}
