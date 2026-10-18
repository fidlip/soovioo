using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SchoolId",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SchoolId",
                table: "TimetableEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SchoolId",
                table: "Subjects",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SchoolId",
                table: "Students",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SchoolId",
                table: "SchoolClasses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SchoolId",
                table: "ParentStudents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SchoolId",
                table: "Messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SchoolId",
                table: "Grades",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SchoolId",
                table: "Absences",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Schools",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Schools", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "SchoolClasses",
                keyColumn: "Id",
                keyValue: 1,
                column: "SchoolId",
                value: 1);

            migrationBuilder.InsertData(
                table: "Schools",
                columns: new[] { "Id", "CreatedAt", "Name", "Slug" },
                values: new object[] { 1, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Demo škola", "demo" });

            migrationBuilder.UpdateData(
                table: "Students",
                keyColumn: "Id",
                keyValue: 1,
                column: "SchoolId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 1,
                column: "SchoolId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 2,
                column: "SchoolId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Subjects",
                keyColumn: "Id",
                keyValue: 3,
                column: "SchoolId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "TimetableEntries",
                keyColumn: "Id",
                keyValue: 1,
                column: "SchoolId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "TimetableEntries",
                keyColumn: "Id",
                keyValue: 2,
                column: "SchoolId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "TimetableEntries",
                keyColumn: "Id",
                keyValue: 3,
                column: "SchoolId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "TimetableEntries",
                keyColumn: "Id",
                keyValue: 4,
                column: "SchoolId",
                value: 1);

            migrationBuilder.CreateIndex(
                name: "IX_Users_SchoolId_Username",
                table: "Users",
                columns: new[] { "SchoolId", "Username" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true,
                filter: "\"SchoolId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TimetableEntries_SchoolId",
                table: "TimetableEntries",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_SchoolId",
                table: "Subjects",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_Students_SchoolId",
                table: "Students",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_SchoolClasses_SchoolId",
                table: "SchoolClasses",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_ParentStudents_SchoolId",
                table: "ParentStudents",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SchoolId",
                table: "Messages",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_Grades_SchoolId",
                table: "Grades",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_Absences_SchoolId",
                table: "Absences",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_Schools_Slug",
                table: "Schools",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Absences_Schools_SchoolId",
                table: "Absences",
                column: "SchoolId",
                principalTable: "Schools",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Grades_Schools_SchoolId",
                table: "Grades",
                column: "SchoolId",
                principalTable: "Schools",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_Schools_SchoolId",
                table: "Messages",
                column: "SchoolId",
                principalTable: "Schools",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ParentStudents_Schools_SchoolId",
                table: "ParentStudents",
                column: "SchoolId",
                principalTable: "Schools",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SchoolClasses_Schools_SchoolId",
                table: "SchoolClasses",
                column: "SchoolId",
                principalTable: "Schools",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Students_Schools_SchoolId",
                table: "Students",
                column: "SchoolId",
                principalTable: "Schools",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Subjects_Schools_SchoolId",
                table: "Subjects",
                column: "SchoolId",
                principalTable: "Schools",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TimetableEntries_Schools_SchoolId",
                table: "TimetableEntries",
                column: "SchoolId",
                principalTable: "Schools",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Schools_SchoolId",
                table: "Users",
                column: "SchoolId",
                principalTable: "Schools",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Absences_Schools_SchoolId",
                table: "Absences");

            migrationBuilder.DropForeignKey(
                name: "FK_Grades_Schools_SchoolId",
                table: "Grades");

            migrationBuilder.DropForeignKey(
                name: "FK_Messages_Schools_SchoolId",
                table: "Messages");

            migrationBuilder.DropForeignKey(
                name: "FK_ParentStudents_Schools_SchoolId",
                table: "ParentStudents");

            migrationBuilder.DropForeignKey(
                name: "FK_SchoolClasses_Schools_SchoolId",
                table: "SchoolClasses");

            migrationBuilder.DropForeignKey(
                name: "FK_Students_Schools_SchoolId",
                table: "Students");

            migrationBuilder.DropForeignKey(
                name: "FK_Subjects_Schools_SchoolId",
                table: "Subjects");

            migrationBuilder.DropForeignKey(
                name: "FK_TimetableEntries_Schools_SchoolId",
                table: "TimetableEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Schools_SchoolId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "Schools");

            migrationBuilder.DropIndex(
                name: "IX_Users_SchoolId_Username",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_Username",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_TimetableEntries_SchoolId",
                table: "TimetableEntries");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_SchoolId",
                table: "Subjects");

            migrationBuilder.DropIndex(
                name: "IX_Students_SchoolId",
                table: "Students");

            migrationBuilder.DropIndex(
                name: "IX_SchoolClasses_SchoolId",
                table: "SchoolClasses");

            migrationBuilder.DropIndex(
                name: "IX_ParentStudents_SchoolId",
                table: "ParentStudents");

            migrationBuilder.DropIndex(
                name: "IX_Messages_SchoolId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Grades_SchoolId",
                table: "Grades");

            migrationBuilder.DropIndex(
                name: "IX_Absences_SchoolId",
                table: "Absences");

            migrationBuilder.DropColumn(
                name: "SchoolId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SchoolId",
                table: "TimetableEntries");

            migrationBuilder.DropColumn(
                name: "SchoolId",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "SchoolId",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "SchoolId",
                table: "SchoolClasses");

            migrationBuilder.DropColumn(
                name: "SchoolId",
                table: "ParentStudents");

            migrationBuilder.DropColumn(
                name: "SchoolId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "SchoolId",
                table: "Grades");

            migrationBuilder.DropColumn(
                name: "SchoolId",
                table: "Absences");
        }
    }
}
