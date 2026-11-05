using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class DivisionCoversPlanRow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CoversSubjectId",
                table: "GroupDivisions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupDivisions_CoversSubjectId",
                table: "GroupDivisions",
                column: "CoversSubjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_GroupDivisions_Subjects_CoversSubjectId",
                table: "GroupDivisions",
                column: "CoversSubjectId",
                principalTable: "Subjects",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GroupDivisions_Subjects_CoversSubjectId",
                table: "GroupDivisions");

            migrationBuilder.DropIndex(
                name: "IX_GroupDivisions_CoversSubjectId",
                table: "GroupDivisions");

            migrationBuilder.DropColumn(
                name: "CoversSubjectId",
                table: "GroupDivisions");
        }
    }
}
