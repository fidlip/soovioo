using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class SupportTicketsInBackoffice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The tickets move to the backoffice without their history: what
            // pointed at them here no longer means anything.
            migrationBuilder.Sql("DELETE FROM \"SupportChanges\"");
            migrationBuilder.Sql("DELETE FROM \"SupportTicketEntries\"");
            migrationBuilder.Sql("UPDATE \"SupportSessions\" SET \"TicketId\" = NULL");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportChanges_SupportTickets_TicketId",
                table: "SupportChanges");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportSessions_SupportTickets_TicketId",
                table: "SupportSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportTicketEntries_SupportTickets_TicketId",
                table: "SupportTicketEntries");

            migrationBuilder.DropTable(
                name: "SupportTicketEntryRevisions");

            migrationBuilder.DropTable(
                name: "SupportTickets");

            migrationBuilder.DropIndex(
                name: "IX_SupportSessions_TicketId",
                table: "SupportSessions");

            migrationBuilder.DropColumn(
                name: "AuthorUserId",
                table: "SupportTicketEntries");

            migrationBuilder.DropColumn(
                name: "BySupport",
                table: "SupportTicketEntries");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "SupportTicketEntries");

            migrationBuilder.DropColumn(
                name: "EditedAt",
                table: "SupportTicketEntries");

            migrationBuilder.DropColumn(
                name: "Internal",
                table: "SupportTicketEntries");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "SupportTicketEntries");

            migrationBuilder.DropColumn(
                name: "Until",
                table: "SupportTicketEntries");

            migrationBuilder.AddColumn<List<int>>(
                name: "SupportAsUserIds",
                table: "SupportSessions",
                type: "integer[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<int>(
                name: "TicketAuthorUserId",
                table: "SupportSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TicketTitle",
                table: "SupportSessions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SupportAsUserIds",
                table: "SupportSessions");

            migrationBuilder.DropColumn(
                name: "TicketAuthorUserId",
                table: "SupportSessions");

            migrationBuilder.DropColumn(
                name: "TicketTitle",
                table: "SupportSessions");

            migrationBuilder.AddColumn<int>(
                name: "AuthorUserId",
                table: "SupportTicketEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BySupport",
                table: "SupportTicketEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "SupportTicketEntries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EditedAt",
                table: "SupportTicketEntries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Internal",
                table: "SupportTicketEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "SupportTicketEntries",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Until",
                table: "SupportTicketEntries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupportTicketEntryRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AuthorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AuthorUserId = table.Column<int>(type: "integer", nullable: true),
                    Change = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EntryId = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTicketEntryRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportTicketEntryRevisions_SupportTicketEntries_EntryId",
                        column: x => x.EntryId,
                        principalTable: "SupportTicketEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupportTickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    AccessRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AccessUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AssigneeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AssigneeUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedBySupport = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PlanName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StatusBeforeCancel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    StatusChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SupportAsUserIds = table.Column<List<int>>(type: "integer[]", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_SupportSessions_TicketId",
                table: "SupportSessions",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketEntryRevisions_EntryId",
                table: "SupportTicketEntryRevisions",
                column: "EntryId");

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
                name: "FK_SupportChanges_SupportTickets_TicketId",
                table: "SupportChanges",
                column: "TicketId",
                principalTable: "SupportTickets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SupportSessions_SupportTickets_TicketId",
                table: "SupportSessions",
                column: "TicketId",
                principalTable: "SupportTickets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTicketEntries_SupportTickets_TicketId",
                table: "SupportTicketEntries",
                column: "TicketId",
                principalTable: "SupportTickets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
