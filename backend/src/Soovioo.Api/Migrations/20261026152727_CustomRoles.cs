using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class CustomRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActiveRoleId",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSuperAdmin",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    BuiltIn = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Roles_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "integer", nullable: false),
                    Permission = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.Permission });
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    RolesId = table.Column<int>(type: "integer", nullable: false),
                    UsersId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.RolesId, x.UsersId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RolesId",
                        column: x => x.RolesId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UsersId",
                        column: x => x.UsersId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_ActiveRoleId",
                table: "Users",
                column: "ActiveRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_SchoolId_BuiltIn",
                table: "Roles",
                columns: new[] { "SchoolId", "BuiltIn" },
                unique: true,
                filter: "\"BuiltIn\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_SchoolId_Name",
                table: "Roles",
                columns: new[] { "SchoolId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_UsersId",
                table: "UserRoles",
                column: "UsersId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Roles_ActiveRoleId",
                table: "Users",
                column: "ActiveRoleId",
                principalTable: "Roles",
                principalColumn: "Id");

            // The fixed roles become every school's built-in ones (their
            // permissions are filled in at startup, RoleSeeder). Old enum:
            // SuperAdmin 0, Admin 1, Teacher 2, Parent 3, Student 4.
            migrationBuilder.Sql("""
                INSERT INTO "Roles" ("SchoolId", "Name", "Kind", "BuiltIn")
                SELECT s."Id", r.name, r.kind, r.builtin
                FROM "Schools" s
                CROSS JOIN LATERAL (VALUES
                    ('Admin', 'Staff', 'Admin'),
                    ('Učitel', 'Staff', 'Teacher'),
                    ('Rodič', 'Parent', 'Parent'),
                    (CASE WHEN s."Type" = 1 THEN 'Student' ELSE 'Žák' END, 'Student', 'Student')
                ) AS r(name, kind, builtin);

                INSERT INTO "UserRoles" ("RolesId", "UsersId")
                SELECT DISTINCT r."Id", u."Id"
                FROM "Users" u
                CROSS JOIN LATERAL unnest(u."Roles") AS ur(role)
                JOIN "Roles" r ON r."SchoolId" = u."SchoolId" AND r."BuiltIn" =
                    CASE ur.role WHEN 1 THEN 'Admin' WHEN 2 THEN 'Teacher' WHEN 3 THEN 'Parent' WHEN 4 THEN 'Student' END;

                UPDATE "Users" u SET "ActiveRoleId" = r."Id"
                FROM "Roles" r
                WHERE r."SchoolId" = u."SchoolId" AND r."BuiltIn" =
                    CASE u."ActiveRole" WHEN 1 THEN 'Admin' WHEN 2 THEN 'Teacher' WHEN 3 THEN 'Parent' WHEN 4 THEN 'Student' END;

                UPDATE "Users" SET "IsSuperAdmin" = TRUE
                WHERE "SchoolId" IS NULL AND ("ActiveRole" = 0 OR 0 = ANY("Roles"));
                """);

            migrationBuilder.DropColumn(
                name: "ActiveRole",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Roles",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActiveRole",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int[]>(
                name: "Roles",
                table: "Users",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            // Back to the fixed roles; a school's own roles go by kind
            // (staff → Teacher).
            migrationBuilder.Sql("""
                UPDATE "Users" u SET "Roles" = COALESCE((
                    SELECT array_agg(DISTINCT CASE COALESCE(r."BuiltIn", CASE r."Kind" WHEN 'Staff' THEN 'Teacher' ELSE r."Kind" END)
                        WHEN 'Admin' THEN 1 WHEN 'Teacher' THEN 2 WHEN 'Parent' THEN 3 ELSE 4 END)
                    FROM "UserRoles" ur JOIN "Roles" r ON r."Id" = ur."RolesId"
                    WHERE ur."UsersId" = u."Id"), '{}');

                UPDATE "Users" u SET "ActiveRole" =
                    CASE COALESCE(r."BuiltIn", CASE r."Kind" WHEN 'Staff' THEN 'Teacher' ELSE r."Kind" END)
                        WHEN 'Admin' THEN 1 WHEN 'Teacher' THEN 2 WHEN 'Parent' THEN 3 ELSE 4 END
                FROM "Roles" r WHERE r."Id" = u."ActiveRoleId";

                UPDATE "Users" SET "ActiveRole" = 0, "Roles" = '{0}' WHERE "IsSuperAdmin";
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Roles_ActiveRoleId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropIndex(
                name: "IX_Users_ActiveRoleId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ActiveRoleId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsSuperAdmin",
                table: "Users");
        }
    }
}
