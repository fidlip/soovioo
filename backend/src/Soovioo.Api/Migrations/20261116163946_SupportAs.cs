using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class SupportAs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<int>>(
                name: "SupportAsUserIds",
                table: "SupportTickets",
                type: "integer[]",
                nullable: false,
                defaultValueSql: "'{}'");

            // The support enters as the author of a school's ticket.
            migrationBuilder.Sql(
                "UPDATE \"SupportTickets\" SET \"SupportAsUserIds\" = ARRAY[\"CreatedByUserId\"] WHERE NOT \"CreatedBySupport\" AND \"CreatedByUserId\" IS NOT NULL");

            migrationBuilder.AddColumn<string>(
                name: "AsName",
                table: "SupportSessions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AsRoleId",
                table: "SupportSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AsUserId",
                table: "SupportSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupportAs",
                table: "Schools",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Users");

            migrationBuilder.AddColumn<int>(
                name: "AsUserId",
                table: "AuditLogEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AsUserName",
                table: "AuditLogEntries",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Schools",
                keyColumn: "Id",
                keyValue: 1,
                column: "SupportAs",
                value: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_AsUserId",
                table: "AuditLogEntries",
                column: "AsUserId",
                filter: "\"AsUserId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogEntries_AsUserId",
                table: "AuditLogEntries");

            migrationBuilder.DropColumn(
                name: "SupportAsUserIds",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "AsName",
                table: "SupportSessions");

            migrationBuilder.DropColumn(
                name: "AsRoleId",
                table: "SupportSessions");

            migrationBuilder.DropColumn(
                name: "AsUserId",
                table: "SupportSessions");

            migrationBuilder.DropColumn(
                name: "SupportAs",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "AsUserId",
                table: "AuditLogEntries");

            migrationBuilder.DropColumn(
                name: "AsUserName",
                table: "AuditLogEntries");
        }
    }
}
