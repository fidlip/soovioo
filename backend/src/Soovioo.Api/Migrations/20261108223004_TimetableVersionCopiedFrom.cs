using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class TimetableVersionCopiedFrom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CopiedFromName",
                table: "TimetableVersions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CopiedFromVersionId",
                table: "TimetableVersions",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "TimetableVersions",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CopiedFromName", "CopiedFromVersionId" },
                values: new object[] { null, null });

            migrationBuilder.CreateIndex(
                name: "IX_TimetableVersions_CopiedFromVersionId",
                table: "TimetableVersions",
                column: "CopiedFromVersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_TimetableVersions_TimetableVersions_CopiedFromVersionId",
                table: "TimetableVersions",
                column: "CopiedFromVersionId",
                principalTable: "TimetableVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TimetableVersions_TimetableVersions_CopiedFromVersionId",
                table: "TimetableVersions");

            migrationBuilder.DropIndex(
                name: "IX_TimetableVersions_CopiedFromVersionId",
                table: "TimetableVersions");

            migrationBuilder.DropColumn(
                name: "CopiedFromName",
                table: "TimetableVersions");

            migrationBuilder.DropColumn(
                name: "CopiedFromVersionId",
                table: "TimetableVersions");
        }
    }
}
