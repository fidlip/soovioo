using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class StudentGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StudentGroupId",
                table: "TimetableEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GroupDivisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    TimetableVersionId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    HoursPerWeek = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: false),
                    DoubleLessons = table.Column<bool>(type: "boolean", nullable: true),
                    ClassIds = table.Column<List<int>>(type: "integer[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupDivisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupDivisions_TimetableVersions_TimetableVersionId",
                        column: x => x.TimetableVersionId,
                        principalTable: "TimetableVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    TimetableVersionId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: false),
                    HoursPerWeek = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: false),
                    DoubleLessons = table.Column<bool>(type: "boolean", nullable: true),
                    TeacherId = table.Column<int>(type: "integer", nullable: true),
                    ClassroomId = table.Column<int>(type: "integer", nullable: true),
                    GroupDivisionId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentGroups_Classrooms_ClassroomId",
                        column: x => x.ClassroomId,
                        principalTable: "Classrooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StudentGroups_GroupDivisions_GroupDivisionId",
                        column: x => x.GroupDivisionId,
                        principalTable: "GroupDivisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentGroups_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentGroups_TimetableVersions_TimetableVersionId",
                        column: x => x.TimetableVersionId,
                        principalTable: "TimetableVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentGroups_Users_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StudentGroupClasses",
                columns: table => new
                {
                    StudentGroupId = table.Column<int>(type: "integer", nullable: false),
                    SchoolClassId = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentGroupClasses", x => new { x.StudentGroupId, x.SchoolClassId });
                    table.ForeignKey(
                        name: "FK_StudentGroupClasses_SchoolClasses_SchoolClassId",
                        column: x => x.SchoolClassId,
                        principalTable: "SchoolClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentGroupClasses_StudentGroups_StudentGroupId",
                        column: x => x.StudentGroupId,
                        principalTable: "StudentGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentGroupMembers",
                columns: table => new
                {
                    StudentGroupId = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentGroupMembers", x => new { x.StudentGroupId, x.StudentId });
                    table.ForeignKey(
                        name: "FK_StudentGroupMembers_StudentGroups_StudentGroupId",
                        column: x => x.StudentGroupId,
                        principalTable: "StudentGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentGroupMembers_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "TimetableEntries",
                keyColumn: "Id",
                keyValue: 1,
                column: "StudentGroupId",
                value: null);

            migrationBuilder.UpdateData(
                table: "TimetableEntries",
                keyColumn: "Id",
                keyValue: 2,
                column: "StudentGroupId",
                value: null);

            migrationBuilder.UpdateData(
                table: "TimetableEntries",
                keyColumn: "Id",
                keyValue: 3,
                column: "StudentGroupId",
                value: null);

            migrationBuilder.UpdateData(
                table: "TimetableEntries",
                keyColumn: "Id",
                keyValue: 4,
                column: "StudentGroupId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_TimetableEntries_StudentGroupId",
                table: "TimetableEntries",
                column: "StudentGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupDivisions_TimetableVersionId",
                table: "GroupDivisions",
                column: "TimetableVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentGroupClasses_SchoolClassId",
                table: "StudentGroupClasses",
                column: "SchoolClassId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentGroupMembers_StudentId",
                table: "StudentGroupMembers",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentGroups_ClassroomId",
                table: "StudentGroups",
                column: "ClassroomId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentGroups_GroupDivisionId",
                table: "StudentGroups",
                column: "GroupDivisionId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentGroups_SubjectId",
                table: "StudentGroups",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentGroups_TeacherId",
                table: "StudentGroups",
                column: "TeacherId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentGroups_TimetableVersionId",
                table: "StudentGroups",
                column: "TimetableVersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_TimetableEntries_StudentGroups_StudentGroupId",
                table: "TimetableEntries",
                column: "StudentGroupId",
                principalTable: "StudentGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TimetableEntries_StudentGroups_StudentGroupId",
                table: "TimetableEntries");

            migrationBuilder.DropTable(
                name: "StudentGroupClasses");

            migrationBuilder.DropTable(
                name: "StudentGroupMembers");

            migrationBuilder.DropTable(
                name: "StudentGroups");

            migrationBuilder.DropTable(
                name: "GroupDivisions");

            migrationBuilder.DropIndex(
                name: "IX_TimetableEntries_StudentGroupId",
                table: "TimetableEntries");

            migrationBuilder.DropColumn(
                name: "StudentGroupId",
                table: "TimetableEntries");
        }
    }
}
