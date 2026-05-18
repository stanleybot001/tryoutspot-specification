using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TryOutSpot.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddListingReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ListingReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReporterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerListingId = table.Column<Guid>(type: "uuid", nullable: true),
                    OpportunityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Pending"),
                    AdminNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListingReports", x => x.Id);
                    table.CheckConstraint("CK_ListingReports_OneListingTarget", "(\"PlayerListingId\" IS NOT NULL AND \"OpportunityId\" IS NULL) OR (\"PlayerListingId\" IS NULL AND \"OpportunityId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ListingReports_Opportunities_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "Opportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ListingReports_PlayerListings_PlayerListingId",
                        column: x => x.PlayerListingId,
                        principalTable: "PlayerListings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ListingReports_Users_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ListingReports_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ListingReports_OpportunityId",
                table: "ListingReports",
                column: "OpportunityId");

            migrationBuilder.CreateIndex(
                name: "IX_ListingReports_OpportunityId_Status_CreatedAt",
                table: "ListingReports",
                columns: new[] { "OpportunityId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ListingReports_PlayerListingId",
                table: "ListingReports",
                column: "PlayerListingId");

            migrationBuilder.CreateIndex(
                name: "IX_ListingReports_PlayerListingId_Status_CreatedAt",
                table: "ListingReports",
                columns: new[] { "PlayerListingId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ListingReports_ReporterUserId",
                table: "ListingReports",
                column: "ReporterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ListingReports_ReporterUserId_OpportunityId_Status",
                table: "ListingReports",
                columns: new[] { "ReporterUserId", "OpportunityId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ListingReports_ReporterUserId_PlayerListingId_Status",
                table: "ListingReports",
                columns: new[] { "ReporterUserId", "PlayerListingId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ListingReports_ReviewedByUserId",
                table: "ListingReports",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ListingReports_Status_CreatedAt",
                table: "ListingReports",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ListingReports");
        }
    }
}
