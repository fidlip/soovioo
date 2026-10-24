using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class StudentDepartureAndRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeactivatedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ErasedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "ErasedCategories",
                table: "Students",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string>(
                name: "LeaveNote",
                table: "Students",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaveReason",
                table: "Students",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LeftAt",
                table: "Students",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RetentionPolicies",
                columns: table => new
                {
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<string>(type: "text", nullable: false),
                    AccountGraceDays = table.Column<int>(type: "integer", nullable: false),
                    PhotosYears = table.Column<int>(type: "integer", nullable: false),
                    ContactsYears = table.Column<int>(type: "integer", nullable: false),
                    MessagesYears = table.Column<int>(type: "integer", nullable: false),
                    HomeworkYears = table.Column<int>(type: "integer", nullable: false),
                    BehaviorYears = table.Column<int>(type: "integer", nullable: false),
                    AttendanceYears = table.Column<int>(type: "integer", nullable: false),
                    GradesYears = table.Column<int>(type: "integer", nullable: false),
                    PaymentsYears = table.Column<int>(type: "integer", nullable: false),
                    ProfileYears = table.Column<int>(type: "integer", nullable: false),
                    StaffYears = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetentionPolicies", x => x.SchoolId);
                    table.ForeignKey(
                        name: "FK_RetentionPolicies_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Students",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "ErasedCategories", "LeaveNote", "LeaveReason", "LeftAt" },
                values: new object[] { new string[0], null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_Users_DeactivatedAt",
                table: "Users",
                column: "DeactivatedAt",
                filter: "\"DeactivatedAt\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RetentionPolicies");

            migrationBuilder.DropIndex(
                name: "IX_Users_DeactivatedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DeactivatedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ErasedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ErasedCategories",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "LeaveNote",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "LeaveReason",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "LeftAt",
                table: "Students");
        }
    }
}
