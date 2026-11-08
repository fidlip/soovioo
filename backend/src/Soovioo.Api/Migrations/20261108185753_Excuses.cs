using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class Excuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CreatedById",
                table: "Absences",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExcuseId",
                table: "Absences",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                table: "Absences",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Excuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    CreatedById = table.Column<int>(type: "integer", nullable: true),
                    DateFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    DateTo = table.Column<DateOnly>(type: "date", nullable: false),
                    Periods = table.Column<List<int>>(type: "integer[]", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Excuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Excuses_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Excuses_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ExcuseAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExcuseId = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExcuseAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExcuseAttachments_Excuses_ExcuseId",
                        column: x => x.ExcuseId,
                        principalTable: "Excuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Absences_CreatedById",
                table: "Absences",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Absences_ExcuseId",
                table: "Absences",
                column: "ExcuseId");

            migrationBuilder.CreateIndex(
                name: "IX_ExcuseAttachments_ExcuseId",
                table: "ExcuseAttachments",
                column: "ExcuseId");

            migrationBuilder.CreateIndex(
                name: "IX_Excuses_CreatedById",
                table: "Excuses",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Excuses_StudentId",
                table: "Excuses",
                column: "StudentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Absences_Excuses_ExcuseId",
                table: "Absences",
                column: "ExcuseId",
                principalTable: "Excuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Absences_Users_CreatedById",
                table: "Absences",
                column: "CreatedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Each excuse given so far becomes a one-day omluvenka, and the
            // notifications about deciding it lead to the omluvenka.
            migrationBuilder.Sql("""
                ALTER TABLE "Excuses" ADD COLUMN "FromAbsenceId" integer;
                INSERT INTO "Excuses" ("SchoolId", "StudentId", "DateFrom", "DateTo", "Periods", "Reason", "CreatedAt", "FromAbsenceId")
                SELECT a."SchoolId", a."StudentId", a."Date", a."Date", '{}', coalesce(left(a."ExcuseReason", 500), ''), a."CreatedAt", a."Id"
                FROM "Absences" a
                WHERE a."ExcuseReason" IS NOT NULL OR a."Status" <> 0;
                UPDATE "Absences" a SET "ExcuseId" = e."Id" FROM "Excuses" e WHERE e."FromAbsenceId" = a."Id";
                UPDATE "Notifications" n SET "TargetId" = e."Id"
                FROM "Excuses" e WHERE n."Type" = 'Excuse' AND n."TargetId" = e."FromAbsenceId";
                ALTER TABLE "Excuses" DROP COLUMN "FromAbsenceId";
                """);

            // Notifications about an omluvenka go with the omluvenka (see
            // NotificationsOfDeletedThings), no longer with its days.
            migrationBuilder.Sql("""
                DROP TRIGGER "Absences_notifications" ON "Absences";
                CREATE TRIGGER "Absences_notifications" AFTER DELETE ON "Absences"
                REFERENCING OLD TABLE AS deleted
                FOR EACH STATEMENT EXECUTE FUNCTION notifications_of_deleted('Absence');
                CREATE TRIGGER "Excuses_notifications" AFTER DELETE ON "Excuses"
                REFERENCING OLD TABLE AS deleted
                FOR EACH STATEMENT EXECUTE FUNCTION notifications_of_deleted('Excuse', 'NewExcuse');
                """);

            migrationBuilder.DropColumn(
                name: "ExcuseReason",
                table: "Absences");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "Excuses_notifications" ON "Excuses";
                DROP TRIGGER "Absences_notifications" ON "Absences";
                CREATE TRIGGER "Absences_notifications" AFTER DELETE ON "Absences"
                REFERENCING OLD TABLE AS deleted
                FOR EACH STATEMENT EXECUTE FUNCTION notifications_of_deleted('Absence', 'Excuse');
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Absences_Excuses_ExcuseId",
                table: "Absences");

            migrationBuilder.DropForeignKey(
                name: "FK_Absences_Users_CreatedById",
                table: "Absences");

            migrationBuilder.DropTable(
                name: "ExcuseAttachments");

            migrationBuilder.DropTable(
                name: "Excuses");

            migrationBuilder.DropIndex(
                name: "IX_Absences_CreatedById",
                table: "Absences");

            migrationBuilder.DropIndex(
                name: "IX_Absences_ExcuseId",
                table: "Absences");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "Absences");

            migrationBuilder.DropColumn(
                name: "ExcuseId",
                table: "Absences");

            migrationBuilder.DropColumn(
                name: "ReviewNote",
                table: "Absences");

            migrationBuilder.AddColumn<string>(
                name: "ExcuseReason",
                table: "Absences",
                type: "text",
                nullable: true);
        }
    }
}
