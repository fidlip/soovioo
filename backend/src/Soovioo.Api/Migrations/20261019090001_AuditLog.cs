using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    Username = table.Column<string>(type: "text", nullable: true),
                    UserDisplayName = table.Column<string>(type: "text", nullable: true),
                    Role = table.Column<string>(type: "text", nullable: true),
                    SupportSessionId = table.Column<int>(type: "integer", nullable: true),
                    SupportBy = table.Column<string>(type: "text", nullable: true),
                    Method = table.Column<string>(type: "text", nullable: false),
                    Path = table.Column<string>(type: "text", nullable: false),
                    Area = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    RequestBody = table.Column<string>(type: "text", nullable: true),
                    Result = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    SchoolClassId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_At",
                table: "AuditLogEntries",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_SchoolId_At",
                table: "AuditLogEntries",
                columns: new[] { "SchoolId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_SupportSessionId",
                table: "AuditLogEntries",
                column: "SupportSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_UserId_At",
                table: "AuditLogEntries",
                columns: new[] { "UserId", "At" });

            // Support-mode changes logged so far move into the general log.
            migrationBuilder.Sql("""
                INSERT INTO "AuditLogEntries"
                    ("At", "SchoolId", "UserId", "Username", "UserDisplayName", "Role", "SupportSessionId",
                     "SupportBy", "Method", "Path", "Area", "StatusCode")
                SELECT a."At", s."SchoolId", s."SuperAdminUserId", s."SuperAdminUsername", s."SuperAdminUsername",
                       'Admin', s."Id", s."SuperAdminUsername", a."Method", a."Path", 'Podpora', a."StatusCode"
                FROM "SupportActions" a JOIN "SupportSessions" s ON s."Id" = a."SupportSessionId";
                """);

            migrationBuilder.DropTable(
                name: "SupportActions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogEntries");

            migrationBuilder.CreateTable(
                name: "SupportActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SupportSessionId = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Method = table.Column<string>(type: "text", nullable: false),
                    Path = table.Column<string>(type: "text", nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportActions_SupportSessions_SupportSessionId",
                        column: x => x.SupportSessionId,
                        principalTable: "SupportSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportActions_SupportSessionId",
                table: "SupportActions",
                column: "SupportSessionId");
        }
    }
}
