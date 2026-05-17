using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TryOutSpot.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyTeamRepresentativeAccountTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[] { new Guid("88888888-8888-8888-8888-888888888888"), "88888888-8888-8888-8888-888888888888", "TeamRepresentative", "TEAMREPRESENTATIVE" });

            migrationBuilder.Sql(
                """
                UPDATE "UserTeamRoles"
                SET "Role" = 'TeamRepresentative'
                WHERE "Role" IN ('Coach', 'TeamManager', 'AcademyDirector', 'OrganizationAdmin');
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
                SELECT DISTINCT legacy."UserId", '88888888-8888-8888-8888-888888888888'::uuid
                FROM "AspNetUserRoles" legacy
                WHERE legacy."RoleId" IN (
                    '33333333-3333-3333-3333-333333333333'::uuid,
                    '44444444-4444-4444-4444-444444444444'::uuid,
                    '55555555-5555-5555-5555-555555555555'::uuid,
                    '66666666-6666-6666-6666-666666666666'::uuid
                )
                AND NOT EXISTS (
                    SELECT 1
                    FROM "AspNetUserRoles" canonical
                    WHERE canonical."UserId" = legacy."UserId"
                      AND canonical."RoleId" = '88888888-8888-8888-8888-888888888888'::uuid
                );
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM "AspNetUserRoles"
                WHERE "RoleId" IN (
                    '33333333-3333-3333-3333-333333333333'::uuid,
                    '44444444-4444-4444-4444-444444444444'::uuid,
                    '55555555-5555-5555-5555-555555555555'::uuid,
                    '66666666-6666-6666-6666-666666666666'::uuid
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
                SELECT DISTINCT canonical."UserId", '33333333-3333-3333-3333-333333333333'::uuid
                FROM "AspNetUserRoles" canonical
                WHERE canonical."RoleId" = '88888888-8888-8888-8888-888888888888'::uuid
                AND NOT EXISTS (
                    SELECT 1
                    FROM "AspNetUserRoles" legacy
                    WHERE legacy."UserId" = canonical."UserId"
                      AND legacy."RoleId" IN (
                        '33333333-3333-3333-3333-333333333333'::uuid,
                        '44444444-4444-4444-4444-444444444444'::uuid,
                        '55555555-5555-5555-5555-555555555555'::uuid,
                        '66666666-6666-6666-6666-666666666666'::uuid
                      )
                );
                """);

            migrationBuilder.Sql(
                """
                UPDATE "UserTeamRoles"
                SET "Role" = 'Coach'
                WHERE "Role" = 'TeamRepresentative';
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM "AspNetUserRoles"
                WHERE "RoleId" = '88888888-8888-8888-8888-888888888888'::uuid;
                """);

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: new Guid("88888888-8888-8888-8888-888888888888"));
        }
    }
}
