using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class MessageBroadcasts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BroadcastId",
                table: "Conversations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MessageBroadcasts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    SenderUserId = table.Column<int>(type: "integer", nullable: true),
                    SenderName = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    AudienceLabel = table.Column<string>(type: "text", nullable: false),
                    StudentsOnly = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageBroadcasts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageBroadcasts_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MessageBroadcasts_Users_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_BroadcastId",
                table: "Conversations",
                column: "BroadcastId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageBroadcasts_SchoolId",
                table: "MessageBroadcasts",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageBroadcasts_SenderUserId",
                table: "MessageBroadcasts",
                column: "SenderUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_MessageBroadcasts_BroadcastId",
                table: "Conversations",
                column: "BroadcastId",
                principalTable: "MessageBroadcasts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Group bulk sends made before broadcasts existed: conversations
            // about a pupil, by the same sender, with the same subject, created
            // within the same minute (MessageBroadcastController's fan-out).
            migrationBuilder.Sql("""
                WITH groups AS (
                    SELECT c."SchoolId", c."CreatedByUserId", c."Subject",
                           date_trunc('minute', c."CreatedAt") AS minute,
                           min(c."CreatedAt") AS created, min(c."Id") AS first_id
                    FROM "Conversations" c
                    WHERE c."CreatedByUserId" IS NOT NULL AND c."StudentId" IS NOT NULL AND c."BroadcastId" IS NULL
                    GROUP BY c."SchoolId", c."CreatedByUserId", c."Subject", date_trunc('minute', c."CreatedAt")
                    HAVING count(*) > 1
                ), inserted AS (
                    INSERT INTO "MessageBroadcasts"
                        ("SchoolId", "SenderUserId", "SenderName", "Subject", "Body", "AudienceLabel", "StudentsOnly", "CreatedAt")
                    SELECT g."SchoolId", g."CreatedByUserId", m."SenderName", g."Subject", m."Body",
                           'adresáti neuvedeni', false, g.created
                    FROM groups g
                    JOIN LATERAL (
                        SELECT "SenderName", "Body" FROM "Messages"
                        WHERE "ConversationId" = g.first_id ORDER BY "SentAt" LIMIT 1
                    ) m ON true
                    RETURNING "Id", "SchoolId", "SenderUserId", "Subject", "CreatedAt"
                )
                UPDATE "Conversations" c SET "BroadcastId" = i."Id"
                FROM inserted i
                WHERE c."SchoolId" = i."SchoolId" AND c."CreatedByUserId" = i."SenderUserId"
                  AND c."Subject" = i."Subject" AND c."StudentId" IS NOT NULL
                  AND date_trunc('minute', c."CreatedAt") = date_trunc('minute', i."CreatedAt");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_MessageBroadcasts_BroadcastId",
                table: "Conversations");

            migrationBuilder.DropTable(
                name: "MessageBroadcasts");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_BroadcastId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "BroadcastId",
                table: "Conversations");
        }
    }
}
