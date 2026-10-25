using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class StaffLeaving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LeaveNote",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaveReason",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LeftAt",
                table: "Users",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "StaffLeaveNotices",
                table: "RetentionPolicies",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.CreateTable(
                name: "StaffHandovers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    TargetId = table.Column<int>(type: "integer", nullable: false),
                    SuccessorId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffHandovers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffHandovers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffHandovers_UserId",
                table: "StaffHandovers",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffHandovers");

            migrationBuilder.DropColumn(
                name: "LeaveNote",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LeaveReason",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LeftAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "StaffLeaveNotices",
                table: "RetentionPolicies");
        }
    }
}
