using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    /// <summary>
    /// Subscription payments previously overloaded Payments.DurationDays to store the purchased
    /// SubscriptionTier's int value instead of a real day count, breaking anything that reads
    /// DurationDays as an actual duration (e.g. invoice line items showing "2 dni" for a Biznes
    /// tier purchase). This adds a dedicated column so DurationDays can stay a genuine day count
    /// for every ServiceType, including Subscription.
    /// </summary>
    public partial class AddSubscriptionTierToPayments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.AddColumnIfMissing("payments", "SubscriptionTier", "int NULL"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MySqlGuard.DropColumnIfExists("payments", "SubscriptionTier"));
        }
    }
}
