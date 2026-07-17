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
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("adverts", "IX_Adverts_IsActive", "`IsActive`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("adverts", "IX_Adverts_IsHidden", "`IsHidden`"));
            // IX_Adverts_UserId already added in AddMissingIndexesAndConstraints migration
            // IX_Adverts_CreatedAt already added in AddMissingIndexesAndConstraints migration

            // B-20: Missing indices on invoices table
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("invoices", "IX_Invoices_UserId", "`UserId`"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("invoices", "IX_Invoices_Month_Year", "`Month`, `Year`"));

            // B-20: IX_Messages_ConversationId already added in AddMissingIndexesAndConstraints migration

            // B-21: FULLTEXT index on adverts(Title, Description) for full-text search
            // Allows future use of MATCH AGAINST syntax instead of LIKE '%term%' full table scans.
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("adverts", "FT_Adverts_TitleDescription", "`Title`, `Description`", "FULLTEXT"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("adverts", "IX_Adverts_IsActive"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("adverts", "IX_Adverts_IsHidden"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("invoices", "IX_Invoices_UserId"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("invoices", "IX_Invoices_Month_Year"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("adverts", "FT_Adverts_TitleDescription"));
        }
    }
}
