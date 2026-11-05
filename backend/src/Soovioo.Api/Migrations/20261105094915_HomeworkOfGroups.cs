using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class HomeworkOfGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "SchoolClassId",
                table: "Homework",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "StudentGroupId",
                table: "Homework",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Homework_StudentGroupId",
                table: "Homework",
                column: "StudentGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_Homework_StudentGroups_StudentGroupId",
                table: "Homework",
                column: "StudentGroupId",
                principalTable: "StudentGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Homework_StudentGroups_StudentGroupId",
                table: "Homework");

            migrationBuilder.DropIndex(
                name: "IX_Homework_StudentGroupId",
                table: "Homework");

            migrationBuilder.DropColumn(
                name: "StudentGroupId",
                table: "Homework");

            migrationBuilder.AlterColumn<int>(
                name: "SchoolClassId",
                table: "Homework",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
