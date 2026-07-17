using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddAuthTokenIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Without indexes, every password reset and email verification performs
            // a full table scan on the users table.
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("users", "UX_Users_PasswordResetToken", "`PasswordResetToken`(255)", "UNIQUE"));
            migrationBuilder.Sql(MySqlGuard.CreateIndexIfMissing("users", "UX_Users_EmailVerificationToken", "`EmailVerificationToken`(255)", "UNIQUE"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("users", "UX_Users_PasswordResetToken"));
            migrationBuilder.Sql(MySqlGuard.DropIndexIfExists("users", "UX_Users_EmailVerificationToken"));
        }
    }
}
