using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class SupportAccessKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HandoffCodeHash",
                table: "SupportSessions");

            migrationBuilder.DropColumn(
                name: "HandoffExpiresAt",
                table: "SupportSessions");

            migrationBuilder.AddColumn<int>(
                name: "AccessKeyId",
                table: "SupportSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OperatorId",
                table: "SupportSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupportAccessKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    TicketId = table.Column<int>(type: "integer", nullable: false),
                    TicketTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TicketAuthorUserId = table.Column<int>(type: "integer", nullable: true),
                    PublicKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OperatorId = table.Column<int>(type: "integer", nullable: false),
                    OperatorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestedAsUserIds = table.Column<List<int>>(type: "integer[]", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedByUserId = table.Column<int>(type: "integer", nullable: true),
                    DecidedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ApprovedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedAsUserIds = table.Column<List<int>>(type: "integer[]", nullable: false),
                    DecisionNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportAccessKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportAccessKeys_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupportEntryChallenges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    KeyId = table.Column<int>(type: "integer", nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AsUserId = table.Column<int>(type: "integer", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportEntryChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportEntryChallenges_SupportAccessKeys_KeyId",
                        column: x => x.KeyId,
                        principalTable: "SupportAccessKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportAccessKeys_SchoolId_Fingerprint",
                table: "SupportAccessKeys",
                columns: new[] { "SchoolId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportAccessKeys_SchoolId_TicketId_CreatedAt",
                table: "SupportAccessKeys",
                columns: new[] { "SchoolId", "TicketId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportEntryChallenges_Hash",
                table: "SupportEntryChallenges",
                column: "Hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportEntryChallenges_KeyId",
                table: "SupportEntryChallenges",
                column: "KeyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportEntryChallenges");

            migrationBuilder.DropTable(
                name: "SupportAccessKeys");

            migrationBuilder.DropColumn(
                name: "AccessKeyId",
                table: "SupportSessions");

            migrationBuilder.DropColumn(
                name: "OperatorId",
                table: "SupportSessions");

            migrationBuilder.AddColumn<string>(
                name: "HandoffCodeHash",
                table: "SupportSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HandoffExpiresAt",
                table: "SupportSessions",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
