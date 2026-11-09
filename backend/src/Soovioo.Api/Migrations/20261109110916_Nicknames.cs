using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class Nicknames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowStudentNicknames",
                table: "Users",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StaffSeeStudentNicknames",
                table: "Schools",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Nickname",
                table: "Persons",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NicknameProposal",
                table: "Persons",
                type: "text",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Schools",
                keyColumn: "Id",
                keyValue: 1,
                column: "StaffSeeStudentNicknames",
                value: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowStudentNicknames",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "StaffSeeStudentNicknames",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "Nickname",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "NicknameProposal",
                table: "Persons");
        }
    }
}
