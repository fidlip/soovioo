using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGradeGradedOn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "GradedOn",
                table: "Grades",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // Existing grades: the best available "given on" date is when
            // they were recorded, in the school's (Czech) local time.
            migrationBuilder.Sql(
                "UPDATE \"Grades\" SET \"GradedOn\" = (\"CreatedAt\" AT TIME ZONE 'Europe/Prague')::date;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GradedOn",
                table: "Grades");
        }
    }
}
