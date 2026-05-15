using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TryOutSpot.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddZipCodeGeographyCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ZipCodeGeographies",
                columns: table => new
                {
                    ZipCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    State = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Latitude = table.Column<decimal>(type: "numeric(9,6)", nullable: false),
                    Longitude = table.Column<decimal>(type: "numeric(9,6)", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZipCodeGeographies", x => x.ZipCode);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ZipCodeGeographies_IsActive",
                table: "ZipCodeGeographies",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ZipCodeGeographies_State_City",
                table: "ZipCodeGeographies",
                columns: new[] { "State", "City" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ZipCodeGeographies");
        }
    }
}
