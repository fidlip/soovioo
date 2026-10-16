using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sova.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEventAudience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_SchoolClasses_SchoolClassId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_SchoolClassId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "SchoolClassId",
                table: "Events");

            migrationBuilder.AddColumn<int>(
                name: "Audience",
                table: "Events",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "EventParents",
                columns: table => new
                {
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventParents", x => new { x.EventId, x.UserId });
                    table.ForeignKey(
                        name: "FK_EventParents_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventParents_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventParents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventSchoolClasses",
                columns: table => new
                {
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    SchoolClassId = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventSchoolClasses", x => new { x.EventId, x.SchoolClassId });
                    table.ForeignKey(
                        name: "FK_EventSchoolClasses_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventSchoolClasses_SchoolClasses_SchoolClassId",
                        column: x => x.SchoolClassId,
                        principalTable: "SchoolClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventSchoolClasses_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventStudents",
                columns: table => new
                {
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventStudents", x => new { x.EventId, x.StudentId });
                    table.ForeignKey(
                        name: "FK_EventStudents_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventStudents_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventStudents_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventTeachers",
                columns: table => new
                {
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventTeachers", x => new { x.EventId, x.UserId });
                    table.ForeignKey(
                        name: "FK_EventTeachers_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventTeachers_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventTeachers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventParents_SchoolId",
                table: "EventParents",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_EventParents_UserId",
                table: "EventParents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_EventSchoolClasses_SchoolClassId",
                table: "EventSchoolClasses",
                column: "SchoolClassId");

            migrationBuilder.CreateIndex(
                name: "IX_EventSchoolClasses_SchoolId",
                table: "EventSchoolClasses",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_EventStudents_SchoolId",
                table: "EventStudents",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_EventStudents_StudentId",
                table: "EventStudents",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_EventTeachers_SchoolId",
                table: "EventTeachers",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_EventTeachers_UserId",
                table: "EventTeachers",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventParents");

            migrationBuilder.DropTable(
                name: "EventSchoolClasses");

            migrationBuilder.DropTable(
                name: "EventStudents");

            migrationBuilder.DropTable(
                name: "EventTeachers");

            migrationBuilder.DropColumn(
                name: "Audience",
                table: "Events");

            migrationBuilder.AddColumn<int>(
                name: "SchoolClassId",
                table: "Events",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_SchoolClassId",
                table: "Events",
                column: "SchoolClassId");

            migrationBuilder.AddForeignKey(
                name: "FK_Events_SchoolClasses_SchoolClassId",
                table: "Events",
                column: "SchoolClassId",
                principalTable: "SchoolClasses",
                principalColumn: "Id");
        }
    }
}
