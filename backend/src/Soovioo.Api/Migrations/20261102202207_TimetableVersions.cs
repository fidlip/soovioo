using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class TimetableVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TimetableVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimetableVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimetableVersions_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddColumn<int>(
                name: "TimetableVersionId",
                table: "TimetableEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Every school's current timetable becomes one published version,
            // valid from the start of this school year so that attendance since
            // September refers to it.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE school_year AS
                SELECT make_date(y, 9, 1) AS start,
                    'Rozvrh ' || y || '/' || lpad(((y + 1) % 100)::text, 2, '0') AS name
                FROM (SELECT EXTRACT(YEAR FROM now() - interval '8 months')::int AS y) t;

                -- The seeded test school keeps the id its seeded lessons point to.
                INSERT INTO "TimetableVersions" ("Id", "SchoolId", "Name", "ValidFrom", "Status", "CreatedAt", "PublishedAt")
                SELECT 1, 1, y.name, y.start, 'Published', TIMESTAMPTZ '2026-01-01 00:00:00+00', TIMESTAMPTZ '2026-01-01 00:00:00+00'
                FROM school_year y
                WHERE EXISTS (SELECT 1 FROM "Schools" WHERE "Id" = 1);

                SELECT setval(pg_get_serial_sequence('"TimetableVersions"', 'Id'),
                    GREATEST((SELECT MAX("Id") FROM "TimetableVersions"), 1));

                INSERT INTO "TimetableVersions" ("SchoolId", "Name", "ValidFrom", "Status", "CreatedAt", "PublishedAt")
                SELECT s."Id", y.name, y.start, 'Published', now(), now()
                FROM "Schools" s CROSS JOIN school_year y
                WHERE NOT EXISTS (SELECT 1 FROM "TimetableVersions" v WHERE v."SchoolId" = s."Id");

                UPDATE "TimetableEntries" e
                SET "TimetableVersionId" = v."Id"
                FROM "TimetableVersions" v
                WHERE v."SchoolId" = e."SchoolId";

                DROP TABLE school_year;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_TimetableEntries_TimetableVersionId",
                table: "TimetableEntries",
                column: "TimetableVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_TimetableVersions_SchoolId_Status_ValidFrom",
                table: "TimetableVersions",
                columns: new[] { "SchoolId", "Status", "ValidFrom" });

            migrationBuilder.AddForeignKey(
                name: "FK_TimetableEntries_TimetableVersions_TimetableVersionId",
                table: "TimetableEntries",
                column: "TimetableVersionId",
                principalTable: "TimetableVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TimetableEntries_TimetableVersions_TimetableVersionId",
                table: "TimetableEntries");

            migrationBuilder.DropTable(
                name: "TimetableVersions");

            migrationBuilder.DropIndex(
                name: "IX_TimetableEntries_TimetableVersionId",
                table: "TimetableEntries");

            migrationBuilder.DropColumn(
                name: "TimetableVersionId",
                table: "TimetableEntries");
        }
    }
}
