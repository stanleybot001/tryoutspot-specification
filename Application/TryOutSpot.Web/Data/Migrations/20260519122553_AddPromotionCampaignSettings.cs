using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TryOutSpot.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotionCampaignSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PromotionCampaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MaxRedemptions = table.Column<int>(type: "integer", nullable: false, defaultValue: 1000),
                    GrantMonths = table.Column<int>(type: "integer", nullable: false, defaultValue: 2),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionCampaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionCampaigns_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PromotionCampaigns_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "PromotionCampaigns",
                columns: new[] { "Id", "Code", "CreatedAt", "CreatedByUserId", "GrantMonths", "IsActive", "MaxRedemptions", "Name", "UpdatedAt", "UpdatedByUserId" },
                values: new object[] { new Guid("58c9ca90-6c60-4dde-b0f7-3d8b6a70ce0c"), "launch_first_1000_two_months", new DateTime(2026, 5, 19, 0, 0, 0, 0, DateTimeKind.Utc), null, 2, true, 1000, "Launch founder offer", new DateTime(2026, 5, 19, 0, 0, 0, 0, DateTimeKind.Utc), null });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionCampaigns_Code",
                table: "PromotionCampaigns",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromotionCampaigns_CreatedByUserId",
                table: "PromotionCampaigns",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionCampaigns_IsActive",
                table: "PromotionCampaigns",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionCampaigns_UpdatedByUserId",
                table: "PromotionCampaigns",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PromotionCampaigns");
        }
    }
}
