using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProfilePhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PhotoConsentAt",
                table: "Students",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoConsentGivenBy",
                table: "Students",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoKey",
                table: "Students",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoStatus",
                table: "Students",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ParentPhotos",
                table: "Schools",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "StudentPhotos",
                table: "Schools",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PhotoKey",
                table: "Persons",
                type: "text",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Schools",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "ParentPhotos", "StudentPhotos" },
                values: new object[] { false, false });

            migrationBuilder.UpdateData(
                table: "Students",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "PhotoConsentAt", "PhotoConsentGivenBy", "PhotoKey", "PhotoStatus" },
                values: new object[] { null, null, null, null });

            // Existing demo schools get what a new demo gets: photos on,
            // staff photos shown to their classes by default.
            migrationBuilder.Sql("""
                UPDATE "Schools" SET "StudentPhotos" = true, "ParentPhotos" = true WHERE "IsDemo";
                UPDATE "VisibilityRules" r SET "Default" = 'MyClasses'
                FROM "Schools" s
                WHERE r."SchoolId" = s."Id" AND s."IsDemo" AND r."Owner" = 'Staff' AND r."Field" = 'Photo';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhotoConsentAt",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "PhotoConsentGivenBy",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "PhotoKey",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "PhotoStatus",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "ParentPhotos",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "StudentPhotos",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "PhotoKey",
                table: "Persons");
        }
    }
}
