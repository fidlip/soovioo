using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonsAndRoleProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ParentStudents_Users_UserId",
                table: "ParentStudents");

            migrationBuilder.CreateTable(
                name: "Persons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: true),
                    FirstName = table.Column<string>(type: "text", nullable: false),
                    LastName = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Persons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Persons_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Persons_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParentProfiles",
                columns: table => new
                {
                    PersonId = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentProfiles", x => x.PersonId);
                    table.ForeignKey(
                        name: "FK_ParentProfiles_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeacherProfiles",
                columns: table => new
                {
                    PersonId = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    Qualification = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherProfiles", x => x.PersonId);
                    table.ForeignKey(
                        name: "FK_TeacherProfiles_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Persons_SchoolId",
                table: "Persons",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_UserId",
                table: "Persons",
                column: "UserId",
                unique: true);

            // Data: one Person per existing account (contacts move over;
            // pupils get their matrika name, everyone else starts with the
            // username as surname), a TeacherProfile per Teacher, a
            // ParentProfile per Parent or anyone with linked children, and
            // the child links re-pointed from the account to the profile.
            migrationBuilder.Sql("""
                INSERT INTO "Persons" ("SchoolId", "FirstName", "LastName", "Phone", "Email", "UserId")
                SELECT u."SchoolId",
                       COALESCE(s."FirstName", ''),
                       COALESCE(s."LastName", u."Username"),
                       u."Phone", u."Email", u."Id"
                FROM "Users" u
                LEFT JOIN "Students" s ON s."UserId" = u."Id";

                INSERT INTO "TeacherProfiles" ("PersonId", "SchoolId")
                SELECT p."Id", p."SchoolId"
                FROM "Persons" p JOIN "Users" u ON u."Id" = p."UserId"
                WHERE 2 = ANY(u."Roles") AND p."SchoolId" IS NOT NULL;

                INSERT INTO "ParentProfiles" ("PersonId", "SchoolId")
                SELECT p."Id", p."SchoolId"
                FROM "Persons" p JOIN "Users" u ON u."Id" = p."UserId"
                WHERE p."SchoolId" IS NOT NULL
                  AND (3 = ANY(u."Roles") OR EXISTS (SELECT 1 FROM "ParentStudents" ps WHERE ps."UserId" = u."Id"));

                UPDATE "ParentStudents" ps SET "UserId" = p."Id"
                FROM "Persons" p WHERE p."UserId" = ps."UserId";
                """);

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Users");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "ParentStudents",
                newName: "ParentProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_ParentStudents_ParentProfiles_ParentProfileId",
                table: "ParentStudents",
                column: "ParentProfileId",
                principalTable: "ParentProfiles",
                principalColumn: "PersonId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ParentStudents_ParentProfiles_ParentProfileId",
                table: "ParentStudents");

            migrationBuilder.DropTable(
                name: "ParentProfiles");

            migrationBuilder.DropTable(
                name: "TeacherProfiles");

            migrationBuilder.DropTable(
                name: "Persons");

            migrationBuilder.RenameColumn(
                name: "ParentProfileId",
                table: "ParentStudents",
                newName: "UserId");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ParentStudents_Users_UserId",
                table: "ParentStudents",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
