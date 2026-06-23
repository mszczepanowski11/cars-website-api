using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddCustomCategoryRequests : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS `customcategoryrequests` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `UserId` varchar(255) NULL,
                    `CategoryName` varchar(200) NOT NULL,
                    `Description` text NULL,
                    `ParametersJson` text NULL,
                    `Status` varchar(20) NOT NULL DEFAULT 'Pending',
                    `AdminNotes` text NULL,
                    `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    `ReviewedAt` datetime(6) NULL,
                    PRIMARY KEY (`Id`),
                    KEY `IX_customcategoryrequests_Status` (`Status`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS `customcategoryrequests`");
        }
    }
}
