using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class TimetableChangeNotice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ChangeNoticeOn",
                table: "TimetableVersions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ChangeNoticeSentAt",
                table: "TimetableVersions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "TimetableVersions",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "ChangeNoticeOn", "ChangeNoticeSentAt" },
                values: new object[] { null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChangeNoticeOn",
                table: "TimetableVersions");

            migrationBuilder.DropColumn(
                name: "ChangeNoticeSentAt",
                table: "TimetableVersions");
        }
    }
}
