using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    /// <summary>
    /// B-20: Add missing performance indices for Adverts, Messages, and Invoices tables.
    /// B-21: Add FULLTEXT index on Adverts(Title, Description) for search performance.
    /// </summary>
    public partial class AddPerformanceIndexesAndFullText : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // B-20: Missing indices on adverts table
            // IsActive + IsHidden cover the "Status" and "IsDeleted" concepts in this schema
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_Adverts_IsActive` ON `adverts` (`IsActive`)");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_Adverts_IsHidden` ON `adverts` (`IsHidden`)");
            // IX_Adverts_UserId already added in AddMissingIndexesAndConstraints migration
            // IX_Adverts_CreatedAt already added in AddMissingIndexesAndConstraints migration

            // B-20: Missing indices on invoices table
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_Invoices_UserId` ON `invoices` (`UserId`)");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_Invoices_Month_Year` ON `invoices` (`Month`, `Year`)");

            // B-20: IX_Messages_ConversationId already added in AddMissingIndexesAndConstraints migration

            // B-21: FULLTEXT index on adverts(Title, Description) for full-text search
            // Allows future use of MATCH AGAINST syntax instead of LIKE '%term%' full table scans.
            migrationBuilder.Sql("ALTER TABLE `adverts` ADD FULLTEXT INDEX IF NOT EXISTS `FT_Adverts_TitleDescription` (`Title`, `Description`)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_Adverts_IsActive` ON `adverts`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_Adverts_IsHidden` ON `adverts`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_Invoices_UserId` ON `invoices`");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_Invoices_Month_Year` ON `invoices`");
            migrationBuilder.Sql("ALTER TABLE `adverts` DROP INDEX IF EXISTS `FT_Adverts_TitleDescription`");
        }
    }
}
