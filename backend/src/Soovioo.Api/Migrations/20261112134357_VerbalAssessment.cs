using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class VerbalAssessment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Kind",
                table: "ReportCardGrades",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "VerbalText",
                table: "ReportCardGrades",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssessmentModeRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    Rocnik = table.Column<int>(type: "integer", nullable: true),
                    StudentId = table.Column<int>(type: "integer", nullable: true),
                    SubjectId = table.Column<int>(type: "integer", nullable: true),
                    Behavior = table.Column<bool>(type: "boolean", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentModeRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentModeRules_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentModeRules_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReportCardNotes",
                columns: table => new
                {
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    SchoolYearStartYear = table.Column<int>(type: "integer", nullable: false),
                    HalfYear = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    BehaviorText = table.Column<string>(type: "text", nullable: true),
                    SummaryText = table.Column<string>(type: "text", nullable: true),
                    SummaryResult = table.Column<int>(type: "integer", nullable: true),
                    SummaryConfirmedByName = table.Column<string>(type: "text", nullable: true),
                    SummaryConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SetByName = table.Column<string>(type: "text", nullable: false),
                    SetAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportCardNotes", x => new { x.StudentId, x.SchoolYearStartYear, x.HalfYear });
                    table.ForeignKey(
                        name: "FK_ReportCardNotes_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentModeRules_SchoolId_Rocnik_StudentId_SubjectId_Beh~",
                table: "AssessmentModeRules",
                columns: new[] { "SchoolId", "Rocnik", "StudentId", "SubjectId", "Behavior" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentModeRules_StudentId",
                table: "AssessmentModeRules",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentModeRules_SubjectId",
                table: "AssessmentModeRules",
                column: "SubjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssessmentModeRules");

            migrationBuilder.DropTable(
                name: "ReportCardNotes");

            migrationBuilder.DropColumn(
                name: "VerbalText",
                table: "ReportCardGrades");

            migrationBuilder.AlterColumn<int>(
                name: "Kind",
                table: "ReportCardGrades",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
