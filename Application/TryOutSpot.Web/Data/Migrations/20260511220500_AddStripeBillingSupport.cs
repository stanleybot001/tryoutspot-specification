using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TryOutSpot.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeBillingSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StripePriceId",
                table: "Subscriptions",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_StripeCustomerId",
                table: "Subscriptions",
                column: "StripeCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_StripeSubscriptionId",
                table: "Subscriptions",
                column: "StripeSubscriptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_StripeCustomerId",
                table: "Subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_StripeSubscriptionId",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "StripePriceId",
                table: "Subscriptions");
        }
    }
}
