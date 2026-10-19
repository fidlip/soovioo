using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class ClassroomShortNameAndType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShortName",
                table: "Classrooms",
                type: "text",
                nullable: true);

            // Free-text types -> preset enum names (string column stays).
            migrationBuilder.Sql("""
                UPDATE "Classrooms" SET "Type" = CASE
                    WHEN "Type" IN ('Homeroom','Specialized','Laboratory','Computer','Gym','Workshop','Other') THEN "Type"
                    WHEN lower("Type") LIKE '%kmenov%' THEN 'Homeroom'
                    WHEN lower("Type") LIKE '%labor%' THEN 'Laboratory'
                    WHEN lower("Type") LIKE '%počítač%' OR lower("Type") LIKE '%pocitac%' OR lower("Type") LIKE '%informat%' THEN 'Computer'
                    WHEN lower("Type") LIKE '%tělocvi%' OR lower("Type") LIKE '%telocvi%' OR lower("Type") LIKE '%gym%' OR lower("Type") LIKE '%sport%' THEN 'Gym'
                    WHEN lower("Type") LIKE '%díln%' OR lower("Type") LIKE '%diln%' THEN 'Workshop'
                    WHEN lower("Type") LIKE '%odborn%' THEN 'Specialized'
                    ELSE 'Other'
                END
                WHERE "Type" IS NOT NULL AND "Type" <> '';
                UPDATE "Classrooms" SET "Type" = NULL WHERE "Type" = '';
                """);

            migrationBuilder.UpdateData(
                table: "Classrooms",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "ShortName", "Type" },
                values: new object[] { null, "Homeroom" });

            migrationBuilder.UpdateData(
                table: "Classrooms",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "ShortName", "Type" },
                values: new object[] { "Tv", "Gym" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShortName",
                table: "Classrooms");

            migrationBuilder.UpdateData(
                table: "Classrooms",
                keyColumn: "Id",
                keyValue: 1,
                column: "Type",
                value: "kmenová");

            migrationBuilder.UpdateData(
                table: "Classrooms",
                keyColumn: "Id",
                keyValue: 2,
                column: "Type",
                value: "tělocvična");
        }
    }
}
