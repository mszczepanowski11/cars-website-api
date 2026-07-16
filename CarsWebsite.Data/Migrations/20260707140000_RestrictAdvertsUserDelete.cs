using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    /// <summary>
    /// A hard delete of a User must not silently cascade-wipe their adverts (and everything
    /// cascading from those - images, conversations, messages with other users). Adverts are
    /// meant to be removed explicitly by the one legitimate hard-delete path (DeletedUserPurgeJob)
    /// before the user row itself is removed.
    /// </summary>
    public partial class RestrictAdvertsUserDelete : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE `adverts` DROP FOREIGN KEY IF EXISTS `FK_Adverts_Users_UserId`");
            migrationBuilder.Sql("ALTER TABLE `adverts` ADD CONSTRAINT `FK_Adverts_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE RESTRICT");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE `adverts` DROP FOREIGN KEY IF EXISTS `FK_Adverts_Users_UserId`");
            migrationBuilder.Sql("ALTER TABLE `adverts` ADD CONSTRAINT `FK_Adverts_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE");
        }
    }
}
