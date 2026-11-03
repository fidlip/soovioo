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

            migrationBuilder.CreateTable(
                name: "SubjectClassrooms",
                columns: table => new
                {
                    SubjectId = table.Column<int>(type: "integer", nullable: false),
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    ClassroomId = table.Column<int>(type: "integer", nullable: true),
                    Type = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubjectClassrooms", x => new { x.SubjectId, x.Id });
                    table.ForeignKey(
                        name: "FK_SubjectClassrooms_Classrooms_ClassroomId",
                        column: x => x.ClassroomId,
                        principalTable: "Classrooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubjectClassrooms_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                @"INSERT INTO ""SubjectClassrooms"" (""SubjectId"", ""Position"", ""ClassroomId"")
                   SELECT ""Id"", 0, ""DefaultClassroomId"" FROM ""Subjects"" WHERE ""DefaultClassroomId"" IS NOT NULL");

            migrationBuilder.DropForeignKey(
                name: "FK_Subjects_Classrooms_DefaultClassroomId",
                table: "Subjects");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_DefaultClassroomId",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "DefaultClassroomId",
                table: "Subjects");

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
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Placed = table.Column<int>(type: "integer", nullable: false),
                    Messages = table.Column<List<string>>(type: "text[]", nullable: false),
                    Notes = table.Column<List<string>>(type: "text[]", nullable: false),
                    Progress = table.Column<string>(type: "jsonb", nullable: true)
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
                name: "IX_SubjectClassrooms_ClassroomId",
                table: "SubjectClassrooms",
                column: "ClassroomId");

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

            migrationBuilder.AddColumn<int>(
                name: "DefaultClassroomId",
                table: "Subjects",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 1,
                column: "DefaultClassroomId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 2,
                column: "DefaultClassroomId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 3,
                column: "DefaultClassroomId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_DefaultClassroomId",
                table: "Subjects",
                column: "DefaultClassroomId");

            migrationBuilder.AddForeignKey(
                name: "FK_Subjects_Classrooms_DefaultClassroomId",
                table: "Subjects",
                column: "DefaultClassroomId",
                principalTable: "Classrooms",
                principalColumn: "Id");

            migrationBuilder.Sql(
                @"UPDATE ""Subjects"" s SET ""DefaultClassroomId"" = c.""ClassroomId""
                   FROM ""SubjectClassrooms"" c
                   WHERE c.""SubjectId"" = s.""Id"" AND c.""ClassroomId"" IS NOT NULL
                     AND c.""Position"" = (SELECT min(f.""Position"") FROM ""SubjectClassrooms"" f
                                         WHERE f.""SubjectId"" = s.""Id"" AND f.""ClassroomId"" IS NOT NULL)");

            migrationBuilder.DropTable(
                name: "SubjectClassrooms");
        }
    }
}
