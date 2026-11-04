using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class ClassHomeroom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HomeroomId",
                table: "SchoolClasses",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SchoolClasses",
                keyColumn: "Id",
                keyValue: 1,
                column: "HomeroomId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_SchoolClasses_HomeroomId",
                table: "SchoolClasses",
                column: "HomeroomId");

            migrationBuilder.AddForeignKey(
                name: "FK_SchoolClasses_Classrooms_HomeroomId",
                table: "SchoolClasses",
                column: "HomeroomId",
                principalTable: "Classrooms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SchoolClasses_Classrooms_HomeroomId",
                table: "SchoolClasses");

            migrationBuilder.DropIndex(
                name: "IX_SchoolClasses_HomeroomId",
                table: "SchoolClasses");

            migrationBuilder.DropColumn(
                name: "HomeroomId",
                table: "SchoolClasses");
        }
    }
}
