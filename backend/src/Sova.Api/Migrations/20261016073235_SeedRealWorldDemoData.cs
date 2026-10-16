using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sova.Api.Migrations
{
    /// <inheritdoc />
    public partial class SeedRealWorldDemoData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SchoolClasses",
                keyColumn: "Id",
                keyValue: 1,
                column: "TeachingLanguage",
                value: "čeština");

            migrationBuilder.UpdateData(
                table: "Schools",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "City", "DirectorName", "Email", "Phone", "PostalCode", "Street" },
                values: new object[] { "Praha 1", "Mgr. Jana Ředitelová", "skola@demo.cz", "+420 123 456 789", "110 00", "Školní 8" });

            migrationBuilder.UpdateData(
                table: "Students",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CatchmentArea", "Citizenship", "City", "ContactEmail", "ContactPhone", "EnrollmentDate", "PostalCode", "Street" },
                values: new object[] { true, "203", "Praha 1", "rodic.novak@example.cz", "+420 777 123 456", new DateOnly(2020, 9, 1), "110 00", "Žákovská 12" });

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 1,
                column: "Abbreviation",
                value: "M");

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 2,
                column: "Abbreviation",
                value: "Čj");

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 3,
                column: "Abbreviation",
                value: "Aj");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SchoolClasses",
                keyColumn: "Id",
                keyValue: 1,
                column: "TeachingLanguage",
                value: null);

            migrationBuilder.UpdateData(
                table: "Schools",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "City", "DirectorName", "Email", "Phone", "PostalCode", "Street" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "Students",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CatchmentArea", "Citizenship", "City", "ContactEmail", "ContactPhone", "EnrollmentDate", "PostalCode", "Street" },
                values: new object[] { null, null, null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 1,
                column: "Abbreviation",
                value: null);

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 2,
                column: "Abbreviation",
                value: null);

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 3,
                column: "Abbreviation",
                value: null);
        }
    }
}
