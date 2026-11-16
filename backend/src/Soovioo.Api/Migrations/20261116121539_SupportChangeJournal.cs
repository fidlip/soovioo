using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class SupportChangeJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SupportSessionId",
                table: "TimetableGenerations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EndReason",
                table: "SupportSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EndedBy",
                table: "SupportSessions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupportChanges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SessionId = table.Column<int>(type: "integer", nullable: false),
                    TicketId = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    KeyJson = table.Column<string>(type: "text", nullable: false),
                    Operation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OriginalJson = table.Column<string>(type: "text", nullable: true),
                    CurrentJson = table.Column<string>(type: "text", nullable: true),
                    ResetAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportChanges_SupportSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "SupportSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SupportChanges_SupportTickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "SupportTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportChanges_SessionId",
                table: "SupportChanges",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportChanges_TicketId_SessionId",
                table: "SupportChanges",
                columns: new[] { "TicketId", "SessionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportChanges");

            migrationBuilder.DropColumn(
                name: "SupportSessionId",
                table: "TimetableGenerations");

            migrationBuilder.DropColumn(
                name: "EndReason",
                table: "SupportSessions");

            migrationBuilder.DropColumn(
                name: "EndedBy",
                table: "SupportSessions");
        }
    }
}
