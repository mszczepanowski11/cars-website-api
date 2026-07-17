using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddKSeFToInvoices : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("invoices", "KSeFReferenceNumber", "varchar(200) NULL"));
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("invoices", "IsKSeFSent", "tinyint(1) NOT NULL DEFAULT 0"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("invoices", "KSeFReferenceNumber"));
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("invoices", "IsKSeFSent"));
        }
    }
}
