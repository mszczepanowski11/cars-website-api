using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    /// <summary>
    /// Business Directory (blueprint section 17): a public, searchable catalogue of automotive /
    /// transport companies, independent of Partner/User. Each row has a global Carizo ID (PublicId).
    /// CREATE TABLE IF NOT EXISTS is inherently idempotent; Program.cs also creates this table via a
    /// startup guard (same belt-and-braces pattern as partners/partnersignuprequests) so it exists
    /// even if the migration chain ever stalls again.
    /// </summary>
    public partial class AddDirectoryCompanies : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS `directorycompanies` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `PublicId` varchar(64) NOT NULL,
                    `Slug` varchar(220) NOT NULL,
                    `Name` varchar(200) NOT NULL,
                    `NameNormalized` varchar(200) NOT NULL,
                    `Category` varchar(60) NOT NULL,
                    `CountryCode` varchar(2) NULL,
                    `City` varchar(120) NULL,
                    `Address` varchar(250) NULL,
                    `PostalCode` varchar(20) NULL,
                    `Phone` varchar(40) NULL,
                    `Email` varchar(200) NULL,
                    `EmailType` varchar(20) NULL,
                    `Website` varchar(300) NULL,
                    `ProfileUrl` varchar(300) NULL,
                    `Language` varchar(5) NULL,
                    `Latitude` double NULL,
                    `Longitude` double NULL,
                    `Status` varchar(20) NOT NULL DEFAULT 'unverified',
                    `Source` varchar(60) NULL,
                    `PartnerId` int NULL,
                    `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    `UpdatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    PRIMARY KEY (`Id`),
                    UNIQUE KEY `IX_directorycompanies_PublicId` (`PublicId`),
                    UNIQUE KEY `IX_directorycompanies_Slug` (`Slug`),
                    KEY `IX_directorycompanies_Category_CountryCode` (`Category`, `CountryCode`),
                    KEY `IX_directorycompanies_NameNormalized` (`NameNormalized`),
                    KEY `IX_directorycompanies_Status` (`Status`),
                    KEY `IX_directorycompanies_PartnerId` (`PartnerId`),
                    CONSTRAINT `FK_directorycompanies_partners_PartnerId` FOREIGN KEY (`PartnerId`) REFERENCES `partners` (`Id`) ON DELETE SET NULL
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS `directorycompanies`;");
        }
    }
}
