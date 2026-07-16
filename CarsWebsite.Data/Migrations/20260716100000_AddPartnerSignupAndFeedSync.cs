using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    /// <summary>
    /// Backs the public self-service "Dla firm" signup flow: a company submits company/contact
    /// info and (optionally) a feed URL via POST /api/partner-signup, which queues a
    /// PartnerSignupRequest for admin review rather than creating anything live. Also adds
    /// FeedUrl/FeedFormat/AutoSyncEnabled to Partners for the pull-based recurring sync
    /// (PartnerFeedSyncJob), separate from the existing push (X-Api-Key) import path.
    /// </summary>
    public partial class AddPartnerSignupAndFeedSync : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE `partners`
                ADD COLUMN IF NOT EXISTS `FeedUrl` varchar(500) NULL,
                ADD COLUMN IF NOT EXISTS `FeedFormat` int NULL,
                ADD COLUMN IF NOT EXISTS `AutoSyncEnabled` tinyint(1) NOT NULL DEFAULT 1;
            ");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS `partnersignuprequests` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `CompanyName` varchar(200) NOT NULL,
                    `Email` varchar(200) NOT NULL,
                    `Phone` varchar(30) NOT NULL,
                    `WebsiteUrl` varchar(300) NULL,
                    `FeedUrl` varchar(500) NULL,
                    `FeedFormat` int NULL,
                    `DetectedItemCount` int NULL,
                    `Status` int NOT NULL DEFAULT 0,
                    `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    `ReviewedAt` datetime(6) NULL,
                    `ReviewedByAdminId` int NULL,
                    `RejectionReason` varchar(500) NULL,
                    `PartnerId` int NULL,
                    PRIMARY KEY (`Id`),
                    KEY `IX_partnersignuprequests_Status` (`Status`),
                    KEY `IX_partnersignuprequests_Email` (`Email`),
                    KEY `IX_partnersignuprequests_PartnerId` (`PartnerId`),
                    CONSTRAINT `FK_partnersignuprequests_partners_PartnerId` FOREIGN KEY (`PartnerId`) REFERENCES `partners` (`Id`) ON DELETE SET NULL
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS `partnersignuprequests`;");
            migrationBuilder.Sql("ALTER TABLE `partners` DROP COLUMN IF EXISTS `AutoSyncEnabled`;");
            migrationBuilder.Sql("ALTER TABLE `partners` DROP COLUMN IF EXISTS `FeedFormat`;");
            migrationBuilder.Sql("ALTER TABLE `partners` DROP COLUMN IF EXISTS `FeedUrl`;");
        }
    }
}
