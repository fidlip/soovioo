using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class SupportTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Result",
                table: "SupportSessions",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TicketId",
                table: "SupportSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupportAccess",
                table: "Schools",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Approval");

            migrationBuilder.CreateTable(
                name: "SupportTickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedBySupport = table.Column<bool>(type: "boolean", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PlanName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StatusChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AssigneeUserId = table.Column<int>(type: "integer", nullable: true),
                    AssigneeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AccessRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AccessUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportTickets_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupportTicketEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TicketId = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AuthorUserId = table.Column<int>(type: "integer", nullable: true),
                    AuthorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BySupport = table.Column<bool>(type: "boolean", nullable: false),
                    Internal = table.Column<bool>(type: "boolean", nullable: false),
                    Text = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SessionId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTicketEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportTicketEntries_SupportTickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "SupportTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Schools",
                keyColumn: "Id",
                keyValue: 1,
                column: "SupportAccess",
                value: "Approval");

            migrationBuilder.CreateIndex(
                name: "IX_SupportSessions_TicketId",
                table: "SupportSessions",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketEntries_TicketId_At",
                table: "SupportTicketEntries",
                columns: new[] { "TicketId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_AssigneeUserId",
                table: "SupportTickets",
                column: "AssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_SchoolId_CreatedAt",
                table: "SupportTickets",
                columns: new[] { "SchoolId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_Status_DueDate",
                table: "SupportTickets",
                columns: new[] { "Status", "DueDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_SupportSessions_SupportTickets_TicketId",
                table: "SupportSessions",
                column: "TicketId",
                principalTable: "SupportTickets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupportSessions_SupportTickets_TicketId",
                table: "SupportSessions");

            migrationBuilder.DropTable(
                name: "SupportTicketEntries");

            migrationBuilder.DropTable(
                name: "SupportTickets");

            migrationBuilder.DropIndex(
                name: "IX_SupportSessions_TicketId",
                table: "SupportSessions");

            migrationBuilder.DropColumn(
                name: "Result",
                table: "SupportSessions");

            migrationBuilder.DropColumn(
                name: "TicketId",
                table: "SupportSessions");

            migrationBuilder.DropColumn(
                name: "SupportAccess",
                table: "Schools");
        }
    }
}
