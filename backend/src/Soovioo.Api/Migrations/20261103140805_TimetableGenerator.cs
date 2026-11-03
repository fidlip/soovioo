using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class TimetableGenerator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Locked",
                table: "TimetableEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ClassroomType",
                table: "Subjects",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TimetableGenerations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    TimetableVersionId = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Placed = table.Column<int>(type: "integer", nullable: false),
                    Messages = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimetableGenerations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimetableGenerations_TimetableVersions_TimetableVersionId",
                        column: x => x.TimetableVersionId,
                        principalTable: "TimetableVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 1,
                column: "ClassroomType",
                value: null);

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 2,
                column: "ClassroomType",
                value: null);

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 3,
                column: "ClassroomType",
                value: null);

            migrationBuilder.UpdateData(
                table: "TimetableEntries",
                keyColumn: "Id",
                keyValue: 1,
                column: "Locked",
                value: false);

            migrationBuilder.UpdateData(
                table: "TimetableEntries",
                keyColumn: "Id",
                keyValue: 2,
                column: "Locked",
                value: false);

            migrationBuilder.UpdateData(
                table: "TimetableEntries",
                keyColumn: "Id",
                keyValue: 3,
                column: "Locked",
                value: false);

            migrationBuilder.UpdateData(
                table: "TimetableEntries",
                keyColumn: "Id",
                keyValue: 4,
                column: "Locked",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_TimetableGenerations_TimetableVersionId",
                table: "TimetableGenerations",
                column: "TimetableVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TimetableGenerations");

            migrationBuilder.DropColumn(
                name: "Locked",
                table: "TimetableEntries");

            migrationBuilder.DropColumn(
                name: "ClassroomType",
                table: "Subjects");
        }
    }
}
