using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddPremiumAdvertFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("CarAdverts", "RegistrationPlate", "varchar(20) NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("CarAdverts", "HasVatInvoice", "tinyint(1) NOT NULL DEFAULT 0"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("CarAdverts", "IsLeasingPossible", "tinyint(1) NOT NULL DEFAULT 0"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("CarAdverts", "IsCreditPossible", "tinyint(1) NOT NULL DEFAULT 0"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("CarAdverts", "IsExchangePossible", "tinyint(1) NOT NULL DEFAULT 0"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("CarAdverts", "GearCount", "int NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("CarAdverts", "MetallicPaint", "tinyint(1) NOT NULL DEFAULT 0"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("CarAdverts", "MaxTrailerWeight", "int NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("CarAdverts", "IsFirstOwner", "tinyint(1) NOT NULL DEFAULT 0"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("CarAdverts", "IsServicedAtASO", "tinyint(1) NOT NULL DEFAULT 0"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("CarAdverts", "IsGaraged", "tinyint(1) NOT NULL DEFAULT 0"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("CarAdverts", "KeyCount", "int NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("CarAdverts", "InsuranceUntil", "datetime(6) NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("CarAdverts", "YoutubeUrl", "varchar(500) NULL"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("CarAdverts", "RegistrationPlate"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("CarAdverts", "HasVatInvoice"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("CarAdverts", "IsLeasingPossible"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("CarAdverts", "IsCreditPossible"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("CarAdverts", "IsExchangePossible"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("CarAdverts", "GearCount"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("CarAdverts", "MetallicPaint"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("CarAdverts", "MaxTrailerWeight"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("CarAdverts", "IsFirstOwner"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("CarAdverts", "IsServicedAtASO"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("CarAdverts", "IsGaraged"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("CarAdverts", "KeyCount"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("CarAdverts", "InsuranceUntil"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("CarAdverts", "YoutubeUrl"));
        }
    }
}
