using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVisibilitySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HiddenFromClassmates",
                table: "Students",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OptOutAllowed",
                table: "Schools",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SharedData",
                table: "Schools",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HiddenData",
                table: "Persons",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Demo schools show everything (Visibility.All / OptOutCapable);
            // real schools start sharing nothing, as before.
            migrationBuilder.Sql("UPDATE \"Schools\" SET \"SharedData\" = 63, \"OptOutAllowed\" = 61 WHERE \"IsDemo\";");

            migrationBuilder.UpdateData(
                table: "Schools",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "OptOutAllowed", "SharedData" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "Students",
                keyColumn: "Id",
                keyValue: 1,
                column: "HiddenFromClassmates",
                value: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HiddenFromClassmates",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "OptOutAllowed",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "SharedData",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "HiddenData",
                table: "Persons");
        }
    }
}
