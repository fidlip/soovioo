using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class Substitutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LessonChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodNumber = table.Column<int>(type: "integer", nullable: false),
                    SchoolClassId = table.Column<int>(type: "integer", nullable: true),
                    StudentGroupId = table.Column<int>(type: "integer", nullable: true),
                    Extra = table.Column<bool>(type: "boolean", nullable: false),
                    Cancelled = table.Column<bool>(type: "boolean", nullable: false),
                    TeacherId = table.Column<int>(type: "integer", nullable: true),
                    SubjectId = table.Column<int>(type: "integer", nullable: true),
                    ClassroomId = table.Column<int>(type: "integer", nullable: true),
                    MovedToDate = table.Column<DateOnly>(type: "date", nullable: true),
                    MovedToPeriod = table.Column<int>(type: "integer", nullable: true),
                    MergedWithClassId = table.Column<int>(type: "integer", nullable: true),
                    MergedWithGroupId = table.Column<int>(type: "integer", nullable: true),
                    OfSubjectId = table.Column<int>(type: "integer", nullable: true),
                    OfTeacherId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonChanges_Classrooms_ClassroomId",
                        column: x => x.ClassroomId,
                        principalTable: "Classrooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LessonChanges_SchoolClasses_SchoolClassId",
                        column: x => x.SchoolClassId,
                        principalTable: "SchoolClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LessonChanges_StudentGroups_StudentGroupId",
                        column: x => x.StudentGroupId,
                        principalTable: "StudentGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LessonChanges_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LessonChanges_Users_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SubstitutionDays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Changes = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubstitutionDays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeacherAbsences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    TeacherId = table.Column<int>(type: "integer", nullable: false),
                    DateFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    DateTo = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodFrom = table.Column<int>(type: "integer", nullable: true),
                    PeriodTo = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherAbsences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeacherAbsences_Users_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LessonChanges_ClassroomId",
                table: "LessonChanges",
                column: "ClassroomId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonChanges_SchoolClassId",
                table: "LessonChanges",
                column: "SchoolClassId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonChanges_SchoolId_Date",
                table: "LessonChanges",
                columns: new[] { "SchoolId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonChanges_StudentGroupId",
                table: "LessonChanges",
                column: "StudentGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonChanges_SubjectId",
                table: "LessonChanges",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonChanges_TeacherId",
                table: "LessonChanges",
                column: "TeacherId");

            migrationBuilder.CreateIndex(
                name: "IX_SubstitutionDays_SchoolId_Date",
                table: "SubstitutionDays",
                columns: new[] { "SchoolId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAbsences_SchoolId_DateFrom",
                table: "TeacherAbsences",
                columns: new[] { "SchoolId", "DateFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAbsences_TeacherId",
                table: "TeacherAbsences",
                column: "TeacherId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LessonChanges");

            migrationBuilder.DropTable(
                name: "SubstitutionDays");

            migrationBuilder.DropTable(
                name: "TeacherAbsences");
        }
    }
}
