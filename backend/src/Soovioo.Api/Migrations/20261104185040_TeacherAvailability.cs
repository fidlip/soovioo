using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class TeacherAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Availability",
                table: "TeacherProfiles",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            // A weekday missing from TeachingDays is a whole day off.
            migrationBuilder.Sql("""
                UPDATE "TeacherProfiles" SET "Availability" = COALESCE((
                    SELECT jsonb_agg(jsonb_build_object('Day', d, 'Period', NULL, 'Kind', 0) ORDER BY d)
                    FROM generate_series(1, 5) AS d
                    WHERE NOT d = ANY("TeachingDays")), '[]'::jsonb);
                """);

            migrationBuilder.DropColumn(
                name: "TeachingDays",
                table: "TeacherProfiles");

            migrationBuilder.AddColumn<int>(
                name: "FreeDays",
                table: "TeacherProfiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxGapsPerDay",
                table: "TeacherProfiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxLessonsPerDay",
                table: "TeacherProfiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinLessonsPerDay",
                table: "TeacherProfiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DoubleLessons",
                table: "Subjects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TeacherMaxGapsPerDay",
                table: "Schools",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeacherMaxLessonsPerDay",
                table: "Schools",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeacherMinLessonsPerDay",
                table: "Schools",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DoubleLessons",
                table: "CurriculumItems",
                type: "boolean",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "HoursPerWeek",
                table: "ClassCurriculumItems",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(4,1)",
                oldPrecision: 4,
                oldScale: 1);

            migrationBuilder.AddColumn<bool>(
                name: "DoubleLessons",
                table: "ClassCurriculumItems",
                type: "boolean",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Schools",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "TeacherMaxGapsPerDay", "TeacherMaxLessonsPerDay", "TeacherMinLessonsPerDay" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 1,
                column: "DoubleLessons",
                value: false);

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 2,
                column: "DoubleLessons",
                value: false);

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 3,
                column: "DoubleLessons",
                value: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int[]>(
                name: "TeachingDays",
                table: "TeacherProfiles",
                type: "integer[]",
                nullable: false,
                defaultValue: new[] { 1, 2, 3, 4, 5 });

            migrationBuilder.Sql("""
                UPDATE "TeacherProfiles" SET "TeachingDays" = ARRAY(
                    SELECT d FROM generate_series(1, 5) AS d
                    WHERE NOT EXISTS (
                        SELECT 1 FROM jsonb_array_elements("Availability") AS a
                        WHERE (a->>'Day')::int = d AND a->'Period' = 'null'::jsonb AND (a->>'Kind')::int = 0));
                """);

            migrationBuilder.DropColumn(
                name: "Availability",
                table: "TeacherProfiles");

            migrationBuilder.DropColumn(
                name: "FreeDays",
                table: "TeacherProfiles");

            migrationBuilder.DropColumn(
                name: "MaxGapsPerDay",
                table: "TeacherProfiles");

            migrationBuilder.DropColumn(
                name: "MaxLessonsPerDay",
                table: "TeacherProfiles");

            migrationBuilder.DropColumn(
                name: "MinLessonsPerDay",
                table: "TeacherProfiles");

            migrationBuilder.DropColumn(
                name: "DoubleLessons",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "TeacherMaxGapsPerDay",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "TeacherMaxLessonsPerDay",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "TeacherMinLessonsPerDay",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "DoubleLessons",
                table: "CurriculumItems");

            migrationBuilder.DropColumn(
                name: "DoubleLessons",
                table: "ClassCurriculumItems");

            migrationBuilder.Sql("""DELETE FROM "ClassCurriculumItems" WHERE "HoursPerWeek" IS NULL;""");

            migrationBuilder.AlterColumn<decimal>(
                name: "HoursPerWeek",
                table: "ClassCurriculumItems",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(4,1)",
                oldPrecision: 4,
                oldScale: 1,
                oldNullable: true);
        }
    }
}
