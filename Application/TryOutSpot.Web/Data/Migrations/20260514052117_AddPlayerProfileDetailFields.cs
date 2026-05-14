using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TryOutSpot.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerProfileDetailFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "PlayerSports"
                SET "SecondaryPositions" = LEFT("SecondaryPositions", 200)
                WHERE "SecondaryPositions" IS NOT NULL
                  AND LENGTH("SecondaryPositions") > 200;
                """);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'PlayerSports'
                          AND column_name = 'SecondaryPositions'
                          AND data_type = 'text') THEN
                        ALTER TABLE "PlayerSports"
                        ALTER COLUMN "SecondaryPositions" TYPE character varying(200);
                    END IF;
                END $$;

                ALTER TABLE "Players"
                ADD COLUMN IF NOT EXISTS "ContactVisibility" character varying(40);

                ALTER TABLE "Players"
                ADD COLUMN IF NOT EXISTS "CurrentTeamName" character varying(200);

                UPDATE "Players"
                SET "ContactVisibility" = 'VerifiedCoachesOnly'
                WHERE COALESCE(NULLIF(BTRIM("ContactVisibility"), ''), '') = '';

                ALTER TABLE "Players"
                ALTER COLUMN "ContactVisibility" SET DEFAULT 'VerifiedCoachesOnly';

                ALTER TABLE "Players"
                ALTER COLUMN "ContactVisibility" SET NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "Players"
                DROP COLUMN IF EXISTS "ContactVisibility";

                ALTER TABLE "Players"
                DROP COLUMN IF EXISTS "CurrentTeamName";

                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'PlayerSports'
                          AND column_name = 'SecondaryPositions'
                          AND data_type = 'character varying') THEN
                        ALTER TABLE "PlayerSports"
                        ALTER COLUMN "SecondaryPositions" TYPE text;
                    END IF;
                END $$;
                """);
        }
    }
}
