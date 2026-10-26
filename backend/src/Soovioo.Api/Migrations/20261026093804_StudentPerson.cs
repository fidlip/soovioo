using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class StudentPerson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Persons_Users_UserId",
                table: "Persons");

            migrationBuilder.DropForeignKey(
                name: "FK_Students_Users_UserId",
                table: "Students");

            migrationBuilder.DropForeignKey(
                name: "FK_VisibilityChoices_Students_StudentId",
                table: "VisibilityChoices");

            migrationBuilder.DropIndex(
                name: "IX_VisibilityChoices_PersonId_Owner_Field_Audience",
                table: "VisibilityChoices");

            migrationBuilder.DropIndex(
                name: "IX_VisibilityChoices_StudentId_Field_Audience",
                table: "VisibilityChoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_VisibilityChoices_OneSubject",
                table: "VisibilityChoices");

            migrationBuilder.DropIndex(
                name: "IX_Students_UserId",
                table: "Students");

            migrationBuilder.AddColumn<string>(
                name: "PhotoStatus",
                table: "Persons",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PersonId",
                table: "Students",
                type: "integer",
                nullable: true);

            // A pupil with an account becomes that account's Person (with the
            // matrika's name and photo); every other pupil gets a new one.
            migrationBuilder.Sql("""
                UPDATE "Students" s SET "PersonId" = p."Id"
                FROM "Persons" p
                WHERE s."UserId" IS NOT NULL AND p."UserId" = s."UserId";

                UPDATE "Persons" p SET
                    "FirstName" = s."FirstName",
                    "LastName" = s."LastName",
                    "PhotoKey" = COALESCE(s."PhotoKey", p."PhotoKey"),
                    "PhotoStatus" = s."PhotoStatus"
                FROM "Students" s
                WHERE s."PersonId" = p."Id";

                ALTER TABLE "Persons" ADD COLUMN "MigratedStudentId" integer;

                INSERT INTO "Persons" ("SchoolId", "FirstName", "LastName", "PhotoKey", "PhotoStatus", "MigratedStudentId")
                SELECT "SchoolId", "FirstName", "LastName", "PhotoKey", "PhotoStatus", "Id"
                FROM "Students"
                WHERE "PersonId" IS NULL;

                UPDATE "Students" s SET "PersonId" = p."Id"
                FROM "Persons" p
                WHERE p."MigratedStudentId" = s."Id";

                ALTER TABLE "Persons" DROP COLUMN "MigratedStudentId";

                DELETE FROM "VisibilityChoices" c
                USING "Students" s
                WHERE c."StudentId" = s."Id"
                    AND EXISTS (
                        SELECT 1 FROM "VisibilityChoices" o
                        WHERE o."PersonId" = s."PersonId"
                            AND o."Owner" = c."Owner" AND o."Field" = c."Field" AND o."Audience" = c."Audience");

                UPDATE "VisibilityChoices" c SET "PersonId" = s."PersonId", "StudentId" = NULL
                FROM "Students" s
                WHERE c."StudentId" = s."Id";
                """);

            migrationBuilder.DropColumn(
                name: "StudentId",
                table: "VisibilityChoices");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "PhotoKey",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "PhotoStatus",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Students");

            migrationBuilder.AlterColumn<int>(
                name: "PersonId",
                table: "VisibilityChoices",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PersonId",
                table: "Students",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VisibilityChoices_PersonId_Owner_Field_Audience",
                table: "VisibilityChoices",
                columns: new[] { "PersonId", "Owner", "Field", "Audience" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Students_PersonId",
                table: "Students",
                column: "PersonId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Persons_Users_UserId",
                table: "Persons",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Students_Persons_PersonId",
                table: "Students",
                column: "PersonId",
                principalTable: "Persons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.RenameColumn(
                name: "Role",
                table: "Users",
                newName: "ActiveRole");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ActiveRole",
                table: "Users",
                newName: "Role");

            migrationBuilder.DropForeignKey(
                name: "FK_Persons_Users_UserId",
                table: "Persons");

            migrationBuilder.DropForeignKey(
                name: "FK_Students_Persons_PersonId",
                table: "Students");

            migrationBuilder.DropIndex(
                name: "IX_VisibilityChoices_PersonId_Owner_Field_Audience",
                table: "VisibilityChoices");

            migrationBuilder.DropIndex(
                name: "IX_Students_PersonId",
                table: "Students");

            migrationBuilder.AlterColumn<int>(
                name: "PersonId",
                table: "VisibilityChoices",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "StudentId",
                table: "VisibilityChoices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "Students",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "Students",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PhotoKey",
                table: "Students",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoStatus",
                table: "Students",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Students",
                type: "integer",
                nullable: true);

            // Back on the matrika; Persons of pupils without an account go.
            migrationBuilder.Sql("""
                UPDATE "Students" s SET
                    "FirstName" = p."FirstName",
                    "LastName" = p."LastName",
                    "PhotoKey" = p."PhotoKey",
                    "PhotoStatus" = p."PhotoStatus",
                    "UserId" = p."UserId"
                FROM "Persons" p
                WHERE p."Id" = s."PersonId";

                UPDATE "VisibilityChoices" c SET "StudentId" = s."Id", "PersonId" = NULL
                FROM "Students" s
                WHERE c."PersonId" = s."PersonId" AND c."Owner" = 'Student';

                DELETE FROM "Persons" p
                USING "Students" s
                WHERE s."PersonId" = p."Id" AND p."UserId" IS NULL;
                """);

            migrationBuilder.DropColumn(
                name: "PersonId",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "PhotoStatus",
                table: "Persons");

            migrationBuilder.CreateIndex(
                name: "IX_VisibilityChoices_PersonId_Owner_Field_Audience",
                table: "VisibilityChoices",
                columns: new[] { "PersonId", "Owner", "Field", "Audience" },
                unique: true,
                filter: "\"PersonId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_VisibilityChoices_StudentId_Field_Audience",
                table: "VisibilityChoices",
                columns: new[] { "StudentId", "Field", "Audience" },
                unique: true,
                filter: "\"StudentId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_VisibilityChoices_OneSubject",
                table: "VisibilityChoices",
                sql: "(\"PersonId\" IS NULL) <> (\"StudentId\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_Students_UserId",
                table: "Students",
                column: "UserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Persons_Users_UserId",
                table: "Persons",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Students_Users_UserId",
                table: "Students",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_VisibilityChoices_Students_StudentId",
                table: "VisibilityChoices",
                column: "StudentId",
                principalTable: "Students",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
