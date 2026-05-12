using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TryOutSpot.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMultipleUserSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_UserId",
                table: "Subscriptions");

            migrationBuilder.AddColumn<Guid>(
                name: "ScopeId",
                table: "Subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScopeType",
                table: "Subscriptions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "account");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_UserId",
                table: "Subscriptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_UserId_PlanType_Scope",
                table: "Subscriptions",
                columns: new[] { "UserId", "PlanType", "ScopeType", "ScopeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_UserId",
                table: "Subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_UserId_PlanType_Scope",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "ScopeId",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "ScopeType",
                table: "Subscriptions");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_UserId",
                table: "Subscriptions",
                column: "UserId",
                unique: true);
        }
    }
}
