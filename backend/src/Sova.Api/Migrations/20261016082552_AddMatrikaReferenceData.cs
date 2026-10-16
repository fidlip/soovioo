using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sova.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMatrikaReferenceData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "City",
                table: "Students",
                newName: "MunicipalityCode");

            migrationBuilder.RenameColumn(
                name: "Citizenship",
                table: "Students",
                newName: "CountryCode");

            migrationBuilder.AddColumn<string>(
                name: "CitizenshipQualifier",
                table: "Students",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Countries",
                columns: table => new
                {
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "Districts",
                columns: table => new
                {
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Districts", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "Municipalities",
                columns: table => new
                {
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    DistrictCode = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Municipalities", x => x.Code);
                    table.ForeignKey(
                        name: "FK_Municipalities_Districts_DistrictCode",
                        column: x => x.DistrictCode,
                        principalTable: "Districts",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "Students",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CitizenshipQualifier", "CountryCode", "MunicipalityCode" },
                values: new object[] { "3", null, null });

            migrationBuilder.CreateIndex(
                name: "IX_Students_CountryCode",
                table: "Students",
                column: "CountryCode");

            migrationBuilder.CreateIndex(
                name: "IX_Students_MunicipalityCode",
                table: "Students",
                column: "MunicipalityCode");

            migrationBuilder.CreateIndex(
                name: "IX_Municipalities_DistrictCode",
                table: "Municipalities",
                column: "DistrictCode");

            migrationBuilder.AddForeignKey(
                name: "FK_Students_Countries_CountryCode",
                table: "Students",
                column: "CountryCode",
                principalTable: "Countries",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Students_Municipalities_MunicipalityCode",
                table: "Students",
                column: "MunicipalityCode",
                principalTable: "Municipalities",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Students_Countries_CountryCode",
                table: "Students");

            migrationBuilder.DropForeignKey(
                name: "FK_Students_Municipalities_MunicipalityCode",
                table: "Students");

            migrationBuilder.DropTable(
                name: "Countries");

            migrationBuilder.DropTable(
                name: "Municipalities");

            migrationBuilder.DropTable(
                name: "Districts");

            migrationBuilder.DropIndex(
                name: "IX_Students_CountryCode",
                table: "Students");

            migrationBuilder.DropIndex(
                name: "IX_Students_MunicipalityCode",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "CitizenshipQualifier",
                table: "Students");

            migrationBuilder.RenameColumn(
                name: "MunicipalityCode",
                table: "Students",
                newName: "City");

            migrationBuilder.RenameColumn(
                name: "CountryCode",
                table: "Students",
                newName: "Citizenship");

            migrationBuilder.UpdateData(
                table: "Students",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Citizenship", "City" },
                values: new object[] { "203", "Praha 1" });
        }
    }
}
