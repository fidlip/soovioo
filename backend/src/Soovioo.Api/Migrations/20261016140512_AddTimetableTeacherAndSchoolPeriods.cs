using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTimetableTeacherAndSchoolPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TeacherId",
                table: "TimetableEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SchoolPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    PeriodNumber = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchoolPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchoolPeriods_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "SchoolPeriods",
                columns: new[] { "Id", "EndTime", "PeriodNumber", "SchoolId", "StartTime" },
                values: new object[,]
                {
                    { 1, new TimeOnly(8, 45, 0), 1, 1, new TimeOnly(8, 0, 0) },
                    { 2, new TimeOnly(9, 40, 0), 2, 1, new TimeOnly(8, 55, 0) },
                    { 3, new TimeOnly(10, 45, 0), 3, 1, new TimeOnly(10, 0, 0) },
                    { 4, new TimeOnly(11, 40, 0), 4, 1, new TimeOnly(10, 55, 0) },
                    { 5, new TimeOnly(12, 35, 0), 5, 1, new TimeOnly(11, 50, 0) },
                    { 6, new TimeOnly(13, 30, 0), 6, 1, new TimeOnly(12, 45, 0) }
                });

            migrationBuilder.UpdateData(
                table: "TimetableEntries",
                keyColumn: "Id",
                keyValue: 1,
                column: "TeacherId",
                value: null);

            migrationBuilder.UpdateData(
                table: "TimetableEntries",
                keyColumn: "Id",
                keyValue: 2,
                column: "TeacherId",
                value: null);

            migrationBuilder.UpdateData(
                table: "TimetableEntries",
                keyColumn: "Id",
                keyValue: 3,
                column: "TeacherId",
                value: null);

            migrationBuilder.UpdateData(
                table: "TimetableEntries",
                keyColumn: "Id",
                keyValue: 4,
                column: "TeacherId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_TimetableEntries_TeacherId",
                table: "TimetableEntries",
                column: "TeacherId");

            migrationBuilder.CreateIndex(
                name: "IX_SchoolPeriods_SchoolId_PeriodNumber",
                table: "SchoolPeriods",
                columns: new[] { "SchoolId", "PeriodNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TimetableEntries_Users_TeacherId",
                table: "TimetableEntries",
                column: "TeacherId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TimetableEntries_Users_TeacherId",
                table: "TimetableEntries");

            migrationBuilder.DropTable(
                name: "SchoolPeriods");

            migrationBuilder.DropIndex(
                name: "IX_TimetableEntries_TeacherId",
                table: "TimetableEntries");

            migrationBuilder.DropColumn(
                name: "TeacherId",
                table: "TimetableEntries");
        }
    }
}
