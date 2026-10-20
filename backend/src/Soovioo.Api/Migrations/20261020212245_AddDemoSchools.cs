using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDemoSchools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "Schools",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDemo",
                table: "Schools",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "Schools",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "ExpiresAt", "IsDemo", "Name", "Slug" },
                values: new object[] { null, false, "Testovací škola", "skola" });

            migrationBuilder.CreateIndex(
                name: "IX_Schools_ExpiresAt",
                table: "Schools",
                column: "ExpiresAt",
                filter: "\"IsDemo\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Schools_ExpiresAt",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "IsDemo",
                table: "Schools");

            migrationBuilder.UpdateData(
                table: "Schools",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Name", "Slug" },
                values: new object[] { "Demo škola", "demo" });
        }
    }
}
