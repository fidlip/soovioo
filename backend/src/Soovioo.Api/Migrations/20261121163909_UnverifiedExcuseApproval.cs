using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class UnverifiedExcuseApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UnverifiedExcuseApproval",
                table: "Schools",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Warn");

            migrationBuilder.UpdateData(
                table: "Schools",
                keyColumn: "Id",
                keyValue: 1,
                column: "UnverifiedExcuseApproval",
                value: "Warn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnverifiedExcuseApproval",
                table: "Schools");
        }
    }
}
