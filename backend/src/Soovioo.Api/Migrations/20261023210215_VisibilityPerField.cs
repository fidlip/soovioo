using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class VisibilityPerField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HiddenFromClassmates",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "OptOutAllowed",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "SharedData",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "HiddenData",
                table: "Persons");

            migrationBuilder.CreateTable(
                name: "VisibilityChoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    PersonId = table.Column<int>(type: "integer", nullable: true),
                    StudentId = table.Column<int>(type: "integer", nullable: true),
                    Owner = table.Column<string>(type: "text", nullable: false),
                    Field = table.Column<string>(type: "text", nullable: false),
                    Audience = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisibilityChoices", x => x.Id);
                    table.CheckConstraint("CK_VisibilityChoices_OneSubject", "(\"PersonId\" IS NULL) <> (\"StudentId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_VisibilityChoices_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VisibilityChoices_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VisibilityRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    Owner = table.Column<string>(type: "text", nullable: false),
                    Field = table.Column<string>(type: "text", nullable: false),
                    Audience = table.Column<string>(type: "text", nullable: false),
                    Min = table.Column<string>(type: "text", nullable: false),
                    Default = table.Column<string>(type: "text", nullable: false),
                    Max = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisibilityRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisibilityRules_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_VisibilityRules_SchoolId_Owner_Field_Audience",
                table: "VisibilityRules",
                columns: new[] { "SchoolId", "Owner", "Field", "Audience" },
                unique: true);

            // Existing demo schools get what Visibility.DemoRules gives new ones.
            migrationBuilder.Sql("""
                INSERT INTO "VisibilityRules" ("SchoolId", "Owner", "Field", "Audience", "Min", "Default", "Max")
                SELECT s."Id", v.owner, v.field, v.audience, 'Nobody', v.def, 'School'
                FROM "Schools" s
                CROSS JOIN (VALUES
                    ('Staff', 'Phone', 'Parents', 'MyClasses'), ('Staff', 'Phone', 'Students', 'MyClasses'),
                    ('Staff', 'Email', 'Parents', 'MyClasses'), ('Staff', 'Email', 'Students', 'MyClasses'),
                    ('Staff', 'Photo', 'Parents', 'Nobody'), ('Staff', 'Photo', 'Students', 'Nobody'),
                    ('Parent', 'Name', 'Parents', 'MyClasses'), ('Parent', 'Phone', 'Parents', 'MyClasses'),
                    ('Parent', 'Email', 'Parents', 'MyClasses'), ('Parent', 'Photo', 'Parents', 'Nobody'),
                    ('Student', 'Name', 'Students', 'MyClasses'), ('Student', 'Name', 'Parents', 'MyClasses'),
                    ('Student', 'Photo', 'Students', 'Nobody'), ('Student', 'Photo', 'Parents', 'Nobody')
                ) AS v(owner, field, audience, def)
                WHERE s."IsDemo";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VisibilityChoices");

            migrationBuilder.DropTable(
                name: "VisibilityRules");

            migrationBuilder.AddColumn<bool>(
                name: "HiddenFromClassmates",
                table: "Students",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OptOutAllowed",
                table: "Schools",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SharedData",
                table: "Schools",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HiddenData",
                table: "Persons",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "Schools",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "OptOutAllowed", "SharedData" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "Students",
                keyColumn: "Id",
                keyValue: 1,
                column: "HiddenFromClassmates",
                value: false);
        }
    }
}
