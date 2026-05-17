using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TryOutSpot.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOpportunityWaiverWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WaiverMethod",
                table: "Opportunities",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WaiverRequired",
                table: "Opportunities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WaiverReturnByEmail",
                table: "Opportunities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WaiverReturnInPerson",
                table: "Opportunities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "WaiverUploadedPdfFileName",
                table: "Opportunities",
                type: "character varying(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WaiverUploadedPdfObjectKey",
                table: "Opportunities",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WaiverMethod",
                table: "Opportunities");

            migrationBuilder.DropColumn(
                name: "WaiverRequired",
                table: "Opportunities");

            migrationBuilder.DropColumn(
                name: "WaiverReturnByEmail",
                table: "Opportunities");

            migrationBuilder.DropColumn(
                name: "WaiverReturnInPerson",
                table: "Opportunities");

            migrationBuilder.DropColumn(
                name: "WaiverUploadedPdfFileName",
                table: "Opportunities");

            migrationBuilder.DropColumn(
                name: "WaiverUploadedPdfObjectKey",
                table: "Opportunities");
        }
    }
}
