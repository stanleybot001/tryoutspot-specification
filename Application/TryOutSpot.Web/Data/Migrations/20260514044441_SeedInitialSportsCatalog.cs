using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TryOutSpot.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedInitialSportsCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO "Sports" ("Id", "Name", "Category", "AgeGroupDivisions", "CompetitionLevels", "TypicalPositions", "IsActive", "CreatedAt")
                VALUES
                    ('a1111111-1111-1111-1111-111111111111', 'Softball', 'Field', NULL, NULL, NULL, TRUE, TIMESTAMPTZ '2026-01-01 00:00:00+00'),
                    ('a2222222-2222-2222-2222-222222222222', 'Baseball', 'Field', NULL, NULL, NULL, TRUE, TIMESTAMPTZ '2026-01-01 00:00:00+00'),
                    ('a3333333-3333-3333-3333-333333333333', 'Soccer', 'Field', NULL, NULL, NULL, TRUE, TIMESTAMPTZ '2026-01-01 00:00:00+00'),
                    ('a4444444-4444-4444-4444-444444444444', 'Basketball', 'Court', NULL, NULL, NULL, TRUE, TIMESTAMPTZ '2026-01-01 00:00:00+00'),
                    ('a5555555-5555-5555-5555-555555555555', 'Volleyball', 'Court', NULL, NULL, NULL, TRUE, TIMESTAMPTZ '2026-01-01 00:00:00+00')
                ON CONFLICT ("Id") DO UPDATE SET
                    "Name" = EXCLUDED."Name",
                    "Category" = EXCLUDED."Category",
                    "IsActive" = EXCLUDED."IsActive";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("a1111111-1111-1111-1111-111111111111"));

            migrationBuilder.DeleteData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("a2222222-2222-2222-2222-222222222222"));

            migrationBuilder.DeleteData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("a3333333-3333-3333-3333-333333333333"));

            migrationBuilder.DeleteData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("a4444444-4444-4444-4444-444444444444"));

            migrationBuilder.DeleteData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: new Guid("a5555555-5555-5555-5555-555555555555"));
        }
    }
}
