using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class SchoolYears : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SchoolYears",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    To = table.Column<DateOnly>(type: "date", nullable: false),
                    TeachingStart = table.Column<DateOnly>(type: "date", nullable: false),
                    TeachingEnd = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchoolYears", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchoolYears_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GradingPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    SchoolYearId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    To = table.Column<DateOnly>(type: "date", nullable: false),
                    Report = table.Column<string>(type: "text", nullable: false),
                    IssuedOn = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradingPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradingPeriods_SchoolYears_SchoolYearId",
                        column: x => x.SchoolYearId,
                        principalTable: "SchoolYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GradingPeriods_SchoolYearId_To",
                table: "GradingPeriods",
                columns: new[] { "SchoolYearId", "To" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchoolYears_SchoolId_From",
                table: "SchoolYears",
                columns: new[] { "SchoolId", "From" },
                unique: true);

            // A school with records gets the years they fall in, up to the
            // one running now; each with its two pololetí, the second to the
            // year's end. Lessons start on 1 Sep (after a weekend on Monday)
            // and end on the last school day up to 30 Jun, when the
            // vysvědčení is issued; the výpis on the last one up to 31 Jan
            // (not a Friday).
            migrationBuilder.Sql("""
                WITH now_year AS (
                    SELECT CASE WHEN EXTRACT(MONTH FROM (now() AT TIME ZONE 'Europe/Prague')) >= 9
                        THEN EXTRACT(YEAR FROM (now() AT TIME ZONE 'Europe/Prague'))::int
                        ELSE EXTRACT(YEAR FROM (now() AT TIME ZONE 'Europe/Prague'))::int - 1 END AS y
                ),
                dated AS (
                    SELECT "SchoolId", "GradedOn" AS d FROM "Grades"
                    UNION ALL SELECT "SchoolId", "Date" FROM "Absences"
                    UNION ALL SELECT "SchoolId", "DateTo" FROM "Excuses"
                    UNION ALL SELECT "SchoolId", "Date" FROM "BehaviorRecords"
                    UNION ALL SELECT "SchoolId", make_date("SchoolYearStartYear", 9, 1) FROM "ReportCardGrades"
                        WHERE "SchoolYearStartYear" BETWEEN 2000 AND 2100
                    UNION ALL SELECT "SchoolId", make_date("SchoolYearStartYear", 9, 1) FROM "BehaviorClassifications"
                        WHERE "SchoolYearStartYear" BETWEEN 2000 AND 2100
                    UNION ALL SELECT "SchoolId", make_date("SchoolYearStartYear", 9, 1) FROM "ReportCardNotes"
                        WHERE "SchoolYearStartYear" BETWEEN 2000 AND 2100
                ),
                years AS (
                    SELECT "SchoolId",
                        CASE WHEN EXTRACT(MONTH FROM d) >= 9 THEN EXTRACT(YEAR FROM d)::int ELSE EXTRACT(YEAR FROM d)::int - 1 END AS y
                    FROM dated
                ),
                span AS (
                    SELECT years."SchoolId", MIN(years.y) AS first, GREATEST(MAX(years.y), now_year.y) AS last
                    FROM years CROSS JOIN now_year
                    WHERE years.y BETWEEN 2000 AND now_year.y + 1
                    GROUP BY years."SchoolId", now_year.y
                )
                INSERT INTO "SchoolYears" ("SchoolId", "Name", "From", "To", "TeachingStart", "TeachingEnd")
                SELECT span."SchoolId",
                    y::text || '/' || lpad(((y + 1) % 100)::text, 2, '0'),
                    make_date(y, 9, 1),
                    make_date(y + 1, 8, 31),
                    make_date(y, 9, 1) + CASE EXTRACT(ISODOW FROM make_date(y, 9, 1))::int WHEN 6 THEN 2 WHEN 7 THEN 1 ELSE 0 END,
                    make_date(y + 1, 6, 30) - CASE EXTRACT(ISODOW FROM make_date(y + 1, 6, 30))::int WHEN 6 THEN 1 WHEN 7 THEN 2 ELSE 0 END
                FROM span CROSS JOIN LATERAL generate_series(span.first, span.last) AS y;

                INSERT INTO "GradingPeriods" ("SchoolId", "SchoolYearId", "Name", "To", "Report", "IssuedOn")
                SELECT "SchoolId", "Id", '1. pololetí', h.d, 'ReportCard',
                    h.d - CASE EXTRACT(ISODOW FROM h.d)::int WHEN 5 THEN 1 WHEN 6 THEN 2 WHEN 7 THEN 3 ELSE 0 END
                FROM "SchoolYears" CROSS JOIN LATERAL (SELECT make_date(EXTRACT(YEAR FROM "From")::int + 1, 1, 31) AS d) h
                UNION ALL
                SELECT "SchoolId", "Id", '2. pololetí', "To", 'ReportCard', "TeachingEnd"
                FROM "SchoolYears";
                """);

            migrationBuilder.AddColumn<int>(
                name: "GradingPeriodId",
                table: "ReportCardNotes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GradingPeriodId",
                table: "ReportCardGrades",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GradingPeriodId",
                table: "BehaviorClassifications",
                type: "integer",
                nullable: true);

            // Year + half become the period; a row of a year no school year
            // was made for (a nonsense year) goes.
            foreach (var table in new[] { "ReportCardNotes", "ReportCardGrades", "BehaviorClassifications" })
            {
                migrationBuilder.Sql($"""
                    UPDATE "{table}" r SET "GradingPeriodId" = p."Id"
                    FROM "SchoolYears" y JOIN "GradingPeriods" p ON p."SchoolYearId" = y."Id"
                    WHERE y."SchoolId" = r."SchoolId"
                        AND EXTRACT(YEAR FROM y."From")::int = r."SchoolYearStartYear"
                        AND EXTRACT(MONTH FROM p."To")::int = CASE r."HalfYear" WHEN 1 THEN 1 ELSE 8 END;

                    DELETE FROM "{table}" WHERE "GradingPeriodId" IS NULL;
                    """);
            }

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReportCardNotes",
                table: "ReportCardNotes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReportCardGrades",
                table: "ReportCardGrades");

            migrationBuilder.DropPrimaryKey(
                name: "PK_BehaviorClassifications",
                table: "BehaviorClassifications");

            foreach (var table in new[] { "ReportCardNotes", "ReportCardGrades", "BehaviorClassifications" })
            {
                migrationBuilder.DropColumn(name: "SchoolYearStartYear", table: table);
                migrationBuilder.DropColumn(name: "HalfYear", table: table);
                migrationBuilder.AlterColumn<int>(
                    name: "GradingPeriodId",
                    table: table,
                    type: "integer",
                    nullable: false,
                    oldClrType: typeof(int),
                    oldType: "integer",
                    oldNullable: true);
            }

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReportCardNotes",
                table: "ReportCardNotes",
                columns: new[] { "StudentId", "GradingPeriodId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReportCardGrades",
                table: "ReportCardGrades",
                columns: new[] { "StudentId", "SubjectId", "GradingPeriodId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_BehaviorClassifications",
                table: "BehaviorClassifications",
                columns: new[] { "StudentId", "GradingPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportCardNotes_GradingPeriodId",
                table: "ReportCardNotes",
                column: "GradingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportCardGrades_GradingPeriodId",
                table: "ReportCardGrades",
                column: "GradingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_BehaviorClassifications_GradingPeriodId",
                table: "BehaviorClassifications",
                column: "GradingPeriodId");

            migrationBuilder.AddForeignKey(
                name: "FK_BehaviorClassifications_GradingPeriods_GradingPeriodId",
                table: "BehaviorClassifications",
                column: "GradingPeriodId",
                principalTable: "GradingPeriods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ReportCardGrades_GradingPeriods_GradingPeriodId",
                table: "ReportCardGrades",
                column: "GradingPeriodId",
                principalTable: "GradingPeriods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ReportCardNotes_GradingPeriods_GradingPeriodId",
                table: "ReportCardNotes",
                column: "GradingPeriodId",
                principalTable: "GradingPeriods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BehaviorClassifications_GradingPeriods_GradingPeriodId",
                table: "BehaviorClassifications");

            migrationBuilder.DropForeignKey(
                name: "FK_ReportCardGrades_GradingPeriods_GradingPeriodId",
                table: "ReportCardGrades");

            migrationBuilder.DropForeignKey(
                name: "FK_ReportCardNotes_GradingPeriods_GradingPeriodId",
                table: "ReportCardNotes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReportCardNotes",
                table: "ReportCardNotes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReportCardGrades",
                table: "ReportCardGrades");

            migrationBuilder.DropPrimaryKey(
                name: "PK_BehaviorClassifications",
                table: "BehaviorClassifications");

            foreach (var table in new[] { "ReportCardNotes", "ReportCardGrades", "BehaviorClassifications" })
            {
                migrationBuilder.DropIndex(name: $"IX_{table}_GradingPeriodId", table: table);

                migrationBuilder.AddColumn<int>(
                    name: "SchoolYearStartYear",
                    table: table,
                    type: "integer",
                    nullable: false,
                    defaultValue: 0);

                migrationBuilder.AddColumn<int>(
                    name: "HalfYear",
                    table: table,
                    type: "integer",
                    nullable: false,
                    defaultValue: 0);

                migrationBuilder.Sql($"""
                    UPDATE "{table}" r
                    SET "SchoolYearStartYear" = EXTRACT(YEAR FROM y."From")::int,
                        "HalfYear" = CASE WHEN EXTRACT(MONTH FROM p."To")::int = 1 THEN 1 ELSE 2 END
                    FROM "GradingPeriods" p JOIN "SchoolYears" y ON y."Id" = p."SchoolYearId"
                    WHERE p."Id" = r."GradingPeriodId";
                    """);

                migrationBuilder.DropColumn(name: "GradingPeriodId", table: table);
            }

            migrationBuilder.DropTable(
                name: "GradingPeriods");

            migrationBuilder.DropTable(
                name: "SchoolYears");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReportCardNotes",
                table: "ReportCardNotes",
                columns: new[] { "StudentId", "SchoolYearStartYear", "HalfYear" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReportCardGrades",
                table: "ReportCardGrades",
                columns: new[] { "StudentId", "SubjectId", "SchoolYearStartYear", "HalfYear" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_BehaviorClassifications",
                table: "BehaviorClassifications",
                columns: new[] { "StudentId", "SchoolYearStartYear", "HalfYear" });
        }
    }
}
