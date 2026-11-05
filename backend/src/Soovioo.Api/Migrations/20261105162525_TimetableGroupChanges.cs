using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class TimetableGroupChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TimetableGroupChanges",
                columns: table => new
                {
                    StudentGroupId = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    Joined = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimetableGroupChanges", x => new { x.StudentGroupId, x.StudentId });
                    table.ForeignKey(
                        name: "FK_TimetableGroupChanges_StudentGroups_StudentGroupId",
                        column: x => x.StudentGroupId,
                        principalTable: "StudentGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TimetableGroupChanges_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TimetableGroupChanges_StudentId",
                table: "TimetableGroupChanges",
                column: "StudentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TimetableGroupChanges");
        }
    }
}
