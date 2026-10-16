using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sova.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRealWorldFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Abbreviation",
                table: "Subjects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CatchmentArea",
                table: "Students",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Citizenship",
                table: "Students",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Students",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "Students",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "Students",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EnrollmentDate",
                table: "Students",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "Students",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Street",
                table: "Students",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Schools",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DirectorName",
                table: "Schools",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Schools",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Schools",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "Schools",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Street",
                table: "Schools",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClassTeacherId",
                table: "SchoolClasses",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TeachingLanguage",
                table: "SchoolClasses",
                type: "text",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SchoolClasses",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "ClassTeacherId", "TeachingLanguage" },
                values: new object[] { null, null });

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

            migrationBuilder.CreateIndex(
                name: "IX_SchoolClasses_ClassTeacherId",
                table: "SchoolClasses",
                column: "ClassTeacherId");

            migrationBuilder.AddForeignKey(
                name: "FK_SchoolClasses_Users_ClassTeacherId",
                table: "SchoolClasses",
                column: "ClassTeacherId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SchoolClasses_Users_ClassTeacherId",
                table: "SchoolClasses");

            migrationBuilder.DropIndex(
                name: "IX_SchoolClasses_ClassTeacherId",
                table: "SchoolClasses");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Abbreviation",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "CatchmentArea",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "Citizenship",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "EnrollmentDate",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "Street",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "DirectorName",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "Street",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "ClassTeacherId",
                table: "SchoolClasses");

            migrationBuilder.DropColumn(
                name: "TeachingLanguage",
                table: "SchoolClasses");
        }
    }
}
