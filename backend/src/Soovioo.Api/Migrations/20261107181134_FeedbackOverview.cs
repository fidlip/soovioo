using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class FeedbackOverview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Not only from demos any more — renamed, the rows kept.
            migrationBuilder.RenameTable(name: "DemoFeedback", newName: "Feedback");
            migrationBuilder.Sql("""ALTER TABLE "Feedback" RENAME CONSTRAINT "PK_DemoFeedback" TO "PK_Feedback";""");
            migrationBuilder.RenameIndex(name: "IX_DemoFeedback_CreatedAt", table: "Feedback", newName: "IX_Feedback_CreatedAt");

            migrationBuilder.AlterColumn<string>(
                name: "DemoSlug",
                table: "Feedback",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(name: "SchoolName", table: "Feedback", type: "text", nullable: true);
            migrationBuilder.AddColumn<int>(name: "UserId", table: "Feedback", type: "integer", nullable: true);
            migrationBuilder.AddColumn<DateTimeOffset>(name: "SeenAt", table: "Feedback", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "Flagged", table: "Feedback", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<DateTimeOffset>(name: "ResolvedAt", table: "Feedback", type: "timestamp with time zone", nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Feedback" f SET "SchoolName" = s."Name"
                FROM "Schools" s WHERE s."Id" = f."SchoolId";
                """);

            migrationBuilder.CreateTable(
                name: "FeedbackReplies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FeedbackId = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    SentBy = table.Column<string>(type: "text", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackReplies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedbackReplies_Feedback_FeedbackId",
                        column: x => x.FeedbackId,
                        principalTable: "Feedback",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackReplies_FeedbackId",
                table: "FeedbackReplies",
                column: "FeedbackId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FeedbackReplies");

            migrationBuilder.Sql("""DELETE FROM "Feedback" WHERE "DemoSlug" IS NULL;""");
            migrationBuilder.DropColumn(name: "SchoolName", table: "Feedback");
            migrationBuilder.DropColumn(name: "UserId", table: "Feedback");
            migrationBuilder.DropColumn(name: "SeenAt", table: "Feedback");
            migrationBuilder.DropColumn(name: "Flagged", table: "Feedback");
            migrationBuilder.DropColumn(name: "ResolvedAt", table: "Feedback");

            migrationBuilder.AlterColumn<string>(
                name: "DemoSlug",
                table: "Feedback",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.RenameIndex(name: "IX_Feedback_CreatedAt", table: "Feedback", newName: "IX_DemoFeedback_CreatedAt");
            migrationBuilder.Sql("""ALTER TABLE "Feedback" RENAME CONSTRAINT "PK_Feedback" TO "PK_DemoFeedback";""");
            migrationBuilder.RenameTable(name: "Feedback", newName: "DemoFeedback");
        }
    }
}
