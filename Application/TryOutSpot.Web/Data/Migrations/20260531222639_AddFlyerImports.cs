using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TryOutSpot.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFlyerImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FlyerImports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePlatform = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "manual"),
                    SourceUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OriginalExternalImageUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    StoredObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    StoredFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    StoredContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "pending_review"),
                    SportId = table.Column<Guid>(type: "uuid", nullable: true),
                    SportName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OpportunityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    TeamName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OrganizationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AgeGroup = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CompetitionLevel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EventDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EventEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RegistrationDeadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RegistrationFee = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    Location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    State = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    ZipCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    WebsiteUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RequiredEquipment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    WhatToBring = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SpecialInstructions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ExtractedJson = table.Column<string>(type: "jsonb", nullable: true),
                    ConfidenceJson = table.Column<string>(type: "jsonb", nullable: true),
                    AdminNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    OpportunityId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlyerImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlyerImports_Opportunities_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "Opportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FlyerImports_Sports_SportId",
                        column: x => x.SportId,
                        principalTable: "Sports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FlyerImports_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FlyerImports_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FlyerImports_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FlyerImports_CreatedByUserId",
                table: "FlyerImports",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FlyerImports_OpportunityId",
                table: "FlyerImports",
                column: "OpportunityId");

            migrationBuilder.CreateIndex(
                name: "IX_FlyerImports_ReviewedByUserId",
                table: "FlyerImports",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FlyerImports_SportId",
                table: "FlyerImports",
                column: "SportId");

            migrationBuilder.CreateIndex(
                name: "IX_FlyerImports_Status_CreatedAt",
                table: "FlyerImports",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FlyerImports_TeamId",
                table: "FlyerImports",
                column: "TeamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FlyerImports");
        }
    }
}
