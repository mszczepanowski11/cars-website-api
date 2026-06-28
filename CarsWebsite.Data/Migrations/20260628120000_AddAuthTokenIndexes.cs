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
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS `UX_Users_PasswordResetToken`
                ON `users` (`PasswordResetToken`(255));
            ");
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS `UX_Users_EmailVerificationToken`
                ON `users` (`EmailVerificationToken`(255));
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS `UX_Users_PasswordResetToken` ON `users`;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `UX_Users_EmailVerificationToken` ON `users`;");
        }
    }
}
