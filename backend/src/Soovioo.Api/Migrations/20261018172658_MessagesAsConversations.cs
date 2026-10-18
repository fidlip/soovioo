using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class MessagesAsConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Messages_Students_StudentId",
                table: "Messages");

            migrationBuilder.AddColumn<int>(
                name: "SenderUserId",
                table: "Messages",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Conversations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastMessageAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Conversations_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Conversations_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Conversations_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ConversationParticipants",
                columns: table => new
                {
                    ConversationId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    LastReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationParticipants", x => new { x.ConversationId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ConversationParticipants_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConversationParticipants_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Data: every old message (one per pupil thread) becomes its own
            // one-message conversation. Participants: the sender (if the
            // SenderName still matches an account), the pupil's parents and
            // the pupil's own account — the people who could see the thread
            // before. The thread's shared ReadAt carries over to each of them.
            migrationBuilder.Sql("""
                ALTER TABLE "Conversations" ADD COLUMN "LegacyMessageId" integer;

                INSERT INTO "Conversations" ("SchoolId", "Subject", "StudentId", "CreatedByUserId", "CreatedAt", "LastMessageAt", "LegacyMessageId")
                SELECT m."SchoolId", m."Subject", m."StudentId", u."Id", m."SentAt", m."SentAt", m."Id"
                FROM "Messages" m
                LEFT JOIN "Users" u ON u."Username" = m."SenderName" AND u."SchoolId" = m."SchoolId";

                INSERT INTO "ConversationParticipants" ("ConversationId", "UserId", "SchoolId", "LastReadAt")
                SELECT c."Id", c."CreatedByUserId", c."SchoolId", c."CreatedAt"
                FROM "Conversations" c WHERE c."CreatedByUserId" IS NOT NULL;

                INSERT INTO "ConversationParticipants" ("ConversationId", "UserId", "SchoolId", "LastReadAt")
                SELECT c."Id", ps."UserId", c."SchoolId", m."ReadAt"
                FROM "Conversations" c
                JOIN "Messages" m ON m."Id" = c."LegacyMessageId"
                JOIN "ParentStudents" ps ON ps."StudentId" = c."StudentId"
                ON CONFLICT DO NOTHING;

                INSERT INTO "ConversationParticipants" ("ConversationId", "UserId", "SchoolId", "LastReadAt")
                SELECT c."Id", s."UserId", c."SchoolId", m."ReadAt"
                FROM "Conversations" c
                JOIN "Messages" m ON m."Id" = c."LegacyMessageId"
                JOIN "Students" s ON s."Id" = c."StudentId"
                WHERE s."UserId" IS NOT NULL
                ON CONFLICT DO NOTHING;

                UPDATE "Messages" m
                SET "StudentId" = c."Id", "SenderUserId" = c."CreatedByUserId"
                FROM "Conversations" c WHERE c."LegacyMessageId" = m."Id";

                ALTER TABLE "Conversations" DROP COLUMN "LegacyMessageId";
                """);

            migrationBuilder.DropColumn(
                name: "ReadAt",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "Subject",
                table: "Messages");

            migrationBuilder.RenameColumn(
                name: "StudentId",
                table: "Messages",
                newName: "ConversationId");

            migrationBuilder.RenameIndex(
                name: "IX_Messages_StudentId",
                table: "Messages",
                newName: "IX_Messages_ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SenderUserId",
                table: "Messages",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationParticipants_UserId",
                table: "ConversationParticipants",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_CreatedByUserId",
                table: "Conversations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_SchoolId",
                table: "Conversations",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_StudentId",
                table: "Conversations",
                column: "StudentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_Conversations_ConversationId",
                table: "Messages",
                column: "ConversationId",
                principalTable: "Conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_Users_SenderUserId",
                table: "Messages",
                column: "SenderUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Messages_Conversations_ConversationId",
                table: "Messages");

            migrationBuilder.DropForeignKey(
                name: "FK_Messages_Users_SenderUserId",
                table: "Messages");

            migrationBuilder.DropTable(
                name: "ConversationParticipants");

            migrationBuilder.DropTable(
                name: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Messages_SenderUserId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "SenderUserId",
                table: "Messages");

            migrationBuilder.RenameColumn(
                name: "ConversationId",
                table: "Messages",
                newName: "StudentId");

            migrationBuilder.RenameIndex(
                name: "IX_Messages_ConversationId",
                table: "Messages",
                newName: "IX_Messages_StudentId");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReadAt",
                table: "Messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Subject",
                table: "Messages",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_Students_StudentId",
                table: "Messages",
                column: "StudentId",
                principalTable: "Students",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
