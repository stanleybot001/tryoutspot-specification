using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TryOutSpot.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamGeographicScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "Teams"
                ADD COLUMN IF NOT EXISTS "GeographicScope" character varying(20);

                UPDATE "Teams"
                SET "GeographicScope" = 'Local'
                WHERE COALESCE(NULLIF(BTRIM("GeographicScope"), ''), '') = '';

                ALTER TABLE "Teams"
                ALTER COLUMN "GeographicScope" SET DEFAULT 'Local';

                ALTER TABLE "Teams"
                ALTER COLUMN "GeographicScope" SET NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "Teams"
                DROP COLUMN IF EXISTS "GeographicScope";
                """);
        }
    }
}
