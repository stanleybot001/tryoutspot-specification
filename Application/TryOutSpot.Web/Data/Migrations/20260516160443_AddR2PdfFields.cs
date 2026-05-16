using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TryOutSpot.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddR2PdfFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UploadedPdfFileName",
                table: "PlayerListings",
                type: "character varying(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UploadedPdfObjectKey",
                table: "PlayerListings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UploadedPdfFileName",
                table: "Opportunities",
                type: "character varying(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UploadedPdfObjectKey",
                table: "Opportunities",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UploadedPdfFileName",
                table: "PlayerListings");

            migrationBuilder.DropColumn(
                name: "UploadedPdfObjectKey",
                table: "PlayerListings");

            migrationBuilder.DropColumn(
                name: "UploadedPdfFileName",
                table: "Opportunities");

            migrationBuilder.DropColumn(
                name: "UploadedPdfObjectKey",
                table: "Opportunities");
        }
    }
}
