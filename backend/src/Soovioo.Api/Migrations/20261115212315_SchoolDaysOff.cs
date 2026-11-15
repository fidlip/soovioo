using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class SchoolDaysOff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "District",
                table: "Schools",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SchoolDaysOff",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    SchoolYearId = table.Column<int>(type: "integer", nullable: false),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    To = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    ClassIds = table.Column<List<int>>(type: "integer[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchoolDaysOff", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchoolDaysOff_SchoolYears_SchoolYearId",
                        column: x => x.SchoolYearId,
                        principalTable: "SchoolYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Schools",
                keyColumn: "Id",
                keyValue: 1,
                column: "District",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_SchoolDaysOff_SchoolYearId_From",
                table: "SchoolDaysOff",
                columns: new[] { "SchoolYearId", "From" });

            // Existing years get MŠMT's prázdniny (the jarní ones need the
            // okres, which nobody has yet) and the public holidays.
            for (var startYear = 2015; startYear <= 2035; startYear++)
            {
                var dates = Soovioo.Api.SchoolYears.MsmtCalendar.For(startYear);
                foreach (var day in Soovioo.Api.SchoolYears.MsmtCalendar.DaysOff(startYear, dates.TeachingStart, dates.SecondHalfIssuedOn, null))
                {
                    migrationBuilder.Sql($"""
                        INSERT INTO "SchoolDaysOff" ("SchoolId", "SchoolYearId", "From", "To", "Name", "Kind", "ClassIds")
                        SELECT y."SchoolId", y."Id", DATE '{day.From:yyyy-MM-dd}', DATE '{day.To:yyyy-MM-dd}', '{day.Name}', '{day.Kind}', ARRAY[]::integer[]
                        FROM "SchoolYears" y
                        WHERE y."From" <= DATE '{day.From:yyyy-MM-dd}' AND DATE '{day.To:yyyy-MM-dd}' <= y."To"
                            AND y."TeachingStart" <= DATE '{day.To:yyyy-MM-dd}' AND DATE '{day.From:yyyy-MM-dd}' <= y."TeachingEnd";
                        """);
                }
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchoolDaysOff");

            migrationBuilder.DropColumn(
                name: "District",
                table: "Schools");
        }
    }
}
