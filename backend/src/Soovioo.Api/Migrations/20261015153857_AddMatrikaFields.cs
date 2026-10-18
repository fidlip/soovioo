using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMatrikaFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DateOfBirth",
                table: "Students",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Gender",
                table: "Students",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RodneCislo",
                table: "Students",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Izo",
                table: "Schools",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RedIzo",
                table: "Schools",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Rocnik",
                table: "SchoolClasses",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SchoolClasses",
                keyColumn: "Id",
                keyValue: 1,
                column: "Rocnik",
                value: 5);

            migrationBuilder.UpdateData(
                table: "Schools",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Izo", "RedIzo" },
                values: new object[] { "308000048", "700000089" });

            migrationBuilder.UpdateData(
                table: "Students",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DateOfBirth", "Gender", "RodneCislo" },
                values: new object[] { new DateOnly(2015, 3, 10), 0, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "RodneCislo",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "Izo",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "RedIzo",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "Rocnik",
                table: "SchoolClasses");
        }
    }
}
