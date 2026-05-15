using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TryOutSpot.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerListings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerListings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SportId = table.Column<Guid>(type: "uuid", nullable: true),
                    ListingType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    AskingPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Condition = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    State = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    ZipCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsSearchable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerListings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerListings_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlayerListings_Sports_SportId",
                        column: x => x.SportId,
                        principalTable: "Sports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlayerListings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerListings_Discovery",
                table: "PlayerListings",
                columns: new[] { "IsPublished", "IsSearchable", "IsActive", "ListingType" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerListings_PlayerId",
                table: "PlayerListings",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerListings_SportId",
                table: "PlayerListings",
                column: "SportId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerListings_UpdatedAt",
                table: "PlayerListings",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerListings_UserId",
                table: "PlayerListings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerListings_ZipCode",
                table: "PlayerListings",
                column: "ZipCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerListings");
        }
    }
}
