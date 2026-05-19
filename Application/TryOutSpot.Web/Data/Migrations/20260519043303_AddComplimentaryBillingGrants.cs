using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TryOutSpot.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddComplimentaryBillingGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComplimentaryPlanGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ScopeType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "account"),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "admin"),
                    PromotionCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    GrantedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokeReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplimentaryPlanGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComplimentaryPlanGrants_Users_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComplimentaryPlanGrants_Users_RevokedByUserId",
                        column: x => x.RevokedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComplimentaryPlanGrants_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PromotionRedemptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedPlanCodes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RedeemedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionRedemptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionRedemptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComplimentaryPlanGrants_GrantedByUserId",
                table: "ComplimentaryPlanGrants",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplimentaryPlanGrants_PromotionCode",
                table: "ComplimentaryPlanGrants",
                column: "PromotionCode");

            migrationBuilder.CreateIndex(
                name: "IX_ComplimentaryPlanGrants_RevokedByUserId",
                table: "ComplimentaryPlanGrants",
                column: "RevokedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplimentaryPlanGrants_User_Status_Window",
                table: "ComplimentaryPlanGrants",
                columns: new[] { "UserId", "RevokedAt", "StartsAt", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ComplimentaryPlanGrants_UserId",
                table: "ComplimentaryPlanGrants",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionRedemptions_PromotionCode",
                table: "PromotionRedemptions",
                column: "PromotionCode");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionRedemptions_PromotionCode_UserId",
                table: "PromotionRedemptions",
                columns: new[] { "PromotionCode", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromotionRedemptions_UserId",
                table: "PromotionRedemptions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComplimentaryPlanGrants");

            migrationBuilder.DropTable(
                name: "PromotionRedemptions");
        }
    }
}
