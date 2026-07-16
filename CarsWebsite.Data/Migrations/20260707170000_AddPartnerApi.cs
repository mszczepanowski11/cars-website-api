using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    /// <summary>
    /// Backs the Partner API (POST /api/partner/adverts/import): companies push their own XML/CSV
    /// inventory feeds, authenticated via X-Api-Key against Partner.ApiKeyHash. Imported adverts are
    /// tagged with PartnerId/ExternalId so repeat imports can match "update" vs "create".
    /// </summary>
    public partial class AddPartnerApi : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS `partners` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `CompanyName` varchar(200) NOT NULL,
                    `ContactEmail` varchar(200) NOT NULL,
                    `ApiKeyHash` varchar(200) NOT NULL,
                    `LinkedUserId` int NOT NULL,
                    `IsActive` tinyint(1) NOT NULL DEFAULT 1,
                    `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    `LastImportAt` datetime(6) NULL,
                    PRIMARY KEY (`Id`),
                    KEY `IX_partners_LinkedUserId` (`LinkedUserId`),
                    CONSTRAINT `FK_partners_users_LinkedUserId` FOREIGN KEY (`LinkedUserId`) REFERENCES `users` (`Id`) ON DELETE RESTRICT
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS `partnerimportlogs` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `PartnerId` int NOT NULL,
                    `Format` int NOT NULL,
                    `StartedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    `CompletedAt` datetime(6) NULL,
                    `ItemsTotal` int NOT NULL DEFAULT 0,
                    `ItemsCreated` int NOT NULL DEFAULT 0,
                    `ItemsUpdated` int NOT NULL DEFAULT 0,
                    `ItemsFailed` int NOT NULL DEFAULT 0,
                    `ErrorSummary` text NULL,
                    PRIMARY KEY (`Id`),
                    KEY `IX_partnerimportlogs_PartnerId` (`PartnerId`),
                    CONSTRAINT `FK_partnerimportlogs_partners_PartnerId` FOREIGN KEY (`PartnerId`) REFERENCES `partners` (`Id`) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE `caradverts`
                ADD COLUMN IF NOT EXISTS `PartnerId` int NULL AFTER `Id`,
                ADD COLUMN IF NOT EXISTS `ExternalId` varchar(200) NULL AFTER `PartnerId`;
            ");

            migrationBuilder.Sql(@"
                SET @idx_exists = (
                    SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'caradverts' AND INDEX_NAME = 'IX_caradverts_PartnerId_ExternalId'
                );
                SET @sql = IF(@idx_exists = 0,
                    'ALTER TABLE `caradverts` ADD UNIQUE INDEX `IX_caradverts_PartnerId_ExternalId` (`PartnerId`, `ExternalId`)',
                    'SELECT 1');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;
            ");

            migrationBuilder.Sql(@"
                SET @fk_exists = (
                    SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'caradverts' AND CONSTRAINT_NAME = 'FK_caradverts_partners_PartnerId'
                );
                SET @sql = IF(@fk_exists = 0,
                    'ALTER TABLE `caradverts` ADD CONSTRAINT `FK_caradverts_partners_PartnerId` FOREIGN KEY (`PartnerId`) REFERENCES `partners` (`Id`) ON DELETE SET NULL',
                    'SELECT 1');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE `caradverts` DROP FOREIGN KEY IF EXISTS `FK_caradverts_partners_PartnerId`;");
            migrationBuilder.Sql("ALTER TABLE `caradverts` DROP INDEX IF EXISTS `IX_caradverts_PartnerId_ExternalId`;");
            migrationBuilder.Sql("ALTER TABLE `caradverts` DROP COLUMN IF EXISTS `ExternalId`;");
            migrationBuilder.Sql("ALTER TABLE `caradverts` DROP COLUMN IF EXISTS `PartnerId`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `partnerimportlogs`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `partners`;");
        }
    }
}
