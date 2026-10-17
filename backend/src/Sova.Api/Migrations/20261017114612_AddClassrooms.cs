using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sova.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddClassrooms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClassroomId",
                table: "TimetableEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultClassroomId",
                table: "Subjects",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Classrooms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: true),
                    Capacity = table.Column<int>(type: "integer", nullable: true),
                    SchoolId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Classrooms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Classrooms_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Classrooms",
                columns: new[] { "Id", "Capacity", "Name", "SchoolId", "Type" },
                values: new object[,]
                {
                    { 1, null, "12", 1, "kmenová" },
                    { 2, 30, "Tělocvična", 1, "tělocvična" }
                });

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 1,
                column: "DefaultClassroomId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 2,
                column: "DefaultClassroomId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 3,
                column: "DefaultClassroomId",
                value: null);

            migrationBuilder.UpdateData(
                table: "TimetableEntries",
                keyColumn: "Id",
                keyValue: 1,
                column: "ClassroomId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "TimetableEntries",
                keyColumn: "Id",
                keyValue: 2,
                column: "ClassroomId",
                value: null);

            migrationBuilder.UpdateData(
                table: "TimetableEntries",
                keyColumn: "Id",
                keyValue: 3,
                column: "ClassroomId",
                value: null);

            migrationBuilder.UpdateData(
                table: "TimetableEntries",
                keyColumn: "Id",
                keyValue: 4,
                column: "ClassroomId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_TimetableEntries_ClassroomId",
                table: "TimetableEntries",
                column: "ClassroomId");

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_DefaultClassroomId",
                table: "Subjects",
                column: "DefaultClassroomId");

            migrationBuilder.CreateIndex(
                name: "IX_Classrooms_SchoolId",
                table: "Classrooms",
                column: "SchoolId");

            migrationBuilder.AddForeignKey(
                name: "FK_Subjects_Classrooms_DefaultClassroomId",
                table: "Subjects",
                column: "DefaultClassroomId",
                principalTable: "Classrooms",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_TimetableEntries_Classrooms_ClassroomId",
                table: "TimetableEntries",
                column: "ClassroomId",
                principalTable: "Classrooms",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Subjects_Classrooms_DefaultClassroomId",
                table: "Subjects");

            migrationBuilder.DropForeignKey(
                name: "FK_TimetableEntries_Classrooms_ClassroomId",
                table: "TimetableEntries");

            migrationBuilder.DropTable(
                name: "Classrooms");

            migrationBuilder.DropIndex(
                name: "IX_TimetableEntries_ClassroomId",
                table: "TimetableEntries");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_DefaultClassroomId",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "ClassroomId",
                table: "TimetableEntries");

            migrationBuilder.DropColumn(
                name: "DefaultClassroomId",
                table: "Subjects");
        }
    }
}
