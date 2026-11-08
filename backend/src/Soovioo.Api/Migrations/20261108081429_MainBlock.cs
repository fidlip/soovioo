using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class MainBlock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MainBlockFrom",
                table: "Schools",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "MainBlockTo",
                table: "Schools",
                type: "integer",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<int>(
                name: "MainBlockFrom",
                table: "SchoolClasses",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MainBlockTo",
                table: "SchoolClasses",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GradeMainBlocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    Grade = table.Column<int>(type: "integer", nullable: false),
                    From = table.Column<int>(type: "integer", nullable: false),
                    To = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeMainBlocks", x => x.Id);
                });



            migrationBuilder.CreateIndex(
                name: "IX_GradeMainBlocks_SchoolId_Grade",
                table: "GradeMainBlocks",
                columns: new[] { "SchoolId", "Grade" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GradeMainBlocks");

            migrationBuilder.DropColumn(
                name: "MainBlockFrom",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "MainBlockTo",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "MainBlockFrom",
                table: "SchoolClasses");

            migrationBuilder.DropColumn(
                name: "MainBlockTo",
                table: "SchoolClasses");
        }
    }
}
