using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AssessmentRuleClassAndFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AssessmentModeRules_SchoolId_Rocnik_StudentId_SubjectId_Beh~",
                table: "AssessmentModeRules");

            migrationBuilder.AddColumn<int>(
                name: "MarkFormat",
                table: "AssessmentModeRules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SchoolClassId",
                table: "AssessmentModeRules",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentModeRules_SchoolClassId",
                table: "AssessmentModeRules",
                column: "SchoolClassId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentModeRules_SchoolId_Rocnik_SchoolClassId_StudentId~",
                table: "AssessmentModeRules",
                columns: new[] { "SchoolId", "Rocnik", "SchoolClassId", "StudentId", "SubjectId", "Behavior" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.AddForeignKey(
                name: "FK_AssessmentModeRules_SchoolClasses_SchoolClassId",
                table: "AssessmentModeRules",
                column: "SchoolClassId",
                principalTable: "SchoolClasses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssessmentModeRules_SchoolClasses_SchoolClassId",
                table: "AssessmentModeRules");

            migrationBuilder.DropIndex(
                name: "IX_AssessmentModeRules_SchoolClassId",
                table: "AssessmentModeRules");

            migrationBuilder.DropIndex(
                name: "IX_AssessmentModeRules_SchoolId_Rocnik_SchoolClassId_StudentId~",
                table: "AssessmentModeRules");

            migrationBuilder.DropColumn(
                name: "MarkFormat",
                table: "AssessmentModeRules");

            migrationBuilder.DropColumn(
                name: "SchoolClassId",
                table: "AssessmentModeRules");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentModeRules_SchoolId_Rocnik_StudentId_SubjectId_Beh~",
                table: "AssessmentModeRules",
                columns: new[] { "SchoolId", "Rocnik", "StudentId", "SubjectId", "Behavior" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }
    }
}
