using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TryOutSpot.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerPerformanceMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdditionalMetrics",
                table: "Players",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CatcherPopTime",
                table: "Players",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExitVelocity",
                table: "Players",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HomeToFirstTime",
                table: "Players",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PitchVelocity",
                table: "Players",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SixtyYardDash",
                table: "Players",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThrowingVelocity",
                table: "Players",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdditionalMetrics",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "CatcherPopTime",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "ExitVelocity",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "HomeToFirstTime",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "PitchVelocity",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "SixtyYardDash",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "ThrowingVelocity",
                table: "Players");
        }
    }
}
