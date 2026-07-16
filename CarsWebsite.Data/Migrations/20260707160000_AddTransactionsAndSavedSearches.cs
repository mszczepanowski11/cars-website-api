using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    /// <summary>
    /// Backs the Transaction (reservation/viewing/purchase) and SavedSearch features - the
    /// frontend (useTransactions.ts, useSavedSearches.ts, transactions.vue, dashboard.vue's
    /// "Wyszukiwania" tab) already called these endpoints; only the backend was missing.
    /// </summary>
    public partial class AddTransactionsAndSavedSearches : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS `transactions` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `Type` int NOT NULL,
                    `Status` int NOT NULL,
                    `AdvertId` int NOT NULL,
                    `BuyerId` int NOT NULL,
                    `SellerId` int NOT NULL,
                    `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    `ScheduledAt` datetime(6) NULL,
                    `CompletedAt` datetime(6) NULL,
                    `Notes` text NULL,
                    PRIMARY KEY (`Id`),
                    KEY `IX_transactions_AdvertId` (`AdvertId`),
                    KEY `IX_transactions_BuyerId` (`BuyerId`),
                    KEY `IX_transactions_SellerId` (`SellerId`),
                    CONSTRAINT `FK_transactions_caradverts_AdvertId` FOREIGN KEY (`AdvertId`) REFERENCES `caradverts` (`Id`) ON DELETE RESTRICT,
                    CONSTRAINT `FK_transactions_users_BuyerId` FOREIGN KEY (`BuyerId`) REFERENCES `users` (`Id`) ON DELETE RESTRICT,
                    CONSTRAINT `FK_transactions_users_SellerId` FOREIGN KEY (`SellerId`) REFERENCES `users` (`Id`) ON DELETE RESTRICT
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS `savedsearches` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `UserId` int NOT NULL,
                    `Name` varchar(200) NOT NULL,
                    `CriteriaJson` longtext NOT NULL,
                    `NotifyOnNew` tinyint(1) NOT NULL DEFAULT 1,
                    `NewResultsCount` int NOT NULL DEFAULT 0,
                    `LastCheckedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    PRIMARY KEY (`Id`),
                    KEY `IX_savedsearches_UserId` (`UserId`),
                    CONSTRAINT `FK_savedsearches_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS `savedsearches`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `transactions`;");
        }
    }
}
