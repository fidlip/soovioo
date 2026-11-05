using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class RvpPrograms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RvpProgramCode",
                table: "SchoolClasses",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubjectRvpAreas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: false),
                    ProgramCode = table.Column<string>(type: "text", nullable: false),
                    AreaKey = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubjectRvpAreas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubjectRvpAreas_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "SchoolClasses",
                keyColumn: "Id",
                keyValue: 1,
                column: "RvpProgramCode",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_SubjectRvpAreas_SubjectId_ProgramCode",
                table: "SubjectRvpAreas",
                columns: new[] { "SubjectId", "ProgramCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubjectRvpAreas");

            migrationBuilder.DropColumn(
                name: "RvpProgramCode",
                table: "SchoolClasses");
        }
    }
}
