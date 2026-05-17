using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TryOutSpot.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserFavorites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserFavorites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerListingId = table.Column<Guid>(type: "uuid", nullable: true),
                    OpportunityId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFavorites", x => x.Id);
                    table.CheckConstraint("CK_UserFavorites_OneFavoriteTarget", "(\"PlayerListingId\" IS NOT NULL AND \"OpportunityId\" IS NULL) OR (\"PlayerListingId\" IS NULL AND \"OpportunityId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_UserFavorites_Opportunities_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "Opportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserFavorites_PlayerListings_PlayerListingId",
                        column: x => x.PlayerListingId,
                        principalTable: "PlayerListings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserFavorites_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserFavorites_OpportunityId",
                table: "UserFavorites",
                column: "OpportunityId");

            migrationBuilder.CreateIndex(
                name: "IX_UserFavorites_PlayerListingId",
                table: "UserFavorites",
                column: "PlayerListingId");

            migrationBuilder.CreateIndex(
                name: "IX_UserFavorites_UserId_CreatedAt",
                table: "UserFavorites",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserFavorites_UserId_OpportunityId",
                table: "UserFavorites",
                columns: new[] { "UserId", "OpportunityId" },
                unique: true,
                filter: "\"OpportunityId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserFavorites_UserId_PlayerListingId",
                table: "UserFavorites",
                columns: new[] { "UserId", "PlayerListingId" },
                unique: true,
                filter: "\"PlayerListingId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserFavorites");
        }
    }
}
