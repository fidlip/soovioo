using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class TeachingAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Qualification",
                table: "TeacherProfiles",
                newName: "QualificationNote");

            migrationBuilder.AddColumn<decimal>(
                name: "ContractedHours",
                table: "TeacherProfiles",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<int[]>(
                name: "TeachingDays",
                table: "TeacherProfiles",
                type: "integer[]",
                nullable: false,
                defaultValue: new[] { 1, 2, 3, 4, 5 });

            migrationBuilder.CreateTable(
                name: "ClassCurriculumItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    TimetableVersionId = table.Column<int>(type: "integer", nullable: false),
                    SchoolClassId = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: false),
                    HoursPerWeek = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassCurriculumItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassCurriculumItems_SchoolClasses_SchoolClassId",
                        column: x => x.SchoolClassId,
                        principalTable: "SchoolClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClassCurriculumItems_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClassCurriculumItems_TimetableVersions_TimetableVersionId",
                        column: x => x.TimetableVersionId,
                        principalTable: "TimetableVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CurriculumItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    TimetableVersionId = table.Column<int>(type: "integer", nullable: false),
                    Grade = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: false),
                    HoursPerWeek = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurriculumItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CurriculumItems_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CurriculumItems_TimetableVersions_TimetableVersionId",
                        column: x => x.TimetableVersionId,
                        principalTable: "TimetableVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeacherQualifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    TeacherProfileId = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: false),
                    GradeFrom = table.Column<int>(type: "integer", nullable: true),
                    GradeTo = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherQualifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeacherQualifications_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeacherQualifications_TeacherProfiles_TeacherProfileId",
                        column: x => x.TeacherProfileId,
                        principalTable: "TeacherProfiles",
                        principalColumn: "PersonId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeachingAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    TimetableVersionId = table.Column<int>(type: "integer", nullable: false),
                    SchoolClassId = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: false),
                    TeacherId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeachingAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeachingAssignments_SchoolClasses_SchoolClassId",
                        column: x => x.SchoolClassId,
                        principalTable: "SchoolClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeachingAssignments_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeachingAssignments_TimetableVersions_TimetableVersionId",
                        column: x => x.TimetableVersionId,
                        principalTable: "TimetableVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeachingAssignments_Users_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClassCurriculumItems_SchoolClassId",
                table: "ClassCurriculumItems",
                column: "SchoolClassId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassCurriculumItems_SubjectId",
                table: "ClassCurriculumItems",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassCurriculumItems_TimetableVersionId_SchoolClassId_Subje~",
                table: "ClassCurriculumItems",
                columns: new[] { "TimetableVersionId", "SchoolClassId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumItems_SubjectId",
                table: "CurriculumItems",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumItems_TimetableVersionId_Grade_SubjectId",
                table: "CurriculumItems",
                columns: new[] { "TimetableVersionId", "Grade", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeacherQualifications_SubjectId",
                table: "TeacherQualifications",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherQualifications_TeacherProfileId_SubjectId",
                table: "TeacherQualifications",
                columns: new[] { "TeacherProfileId", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeachingAssignments_SchoolClassId",
                table: "TeachingAssignments",
                column: "SchoolClassId");

            migrationBuilder.CreateIndex(
                name: "IX_TeachingAssignments_SubjectId",
                table: "TeachingAssignments",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_TeachingAssignments_TeacherId",
                table: "TeachingAssignments",
                column: "TeacherId");

            migrationBuilder.CreateIndex(
                name: "IX_TeachingAssignments_TimetableVersionId_SchoolClassId_Subjec~",
                table: "TeachingAssignments",
                columns: new[] { "TimetableVersionId", "SchoolClassId", "SubjectId" },
                unique: true);

            // Aprobace was free text: names and abbreviations of the school's
            // subjects become its subjects, the rest stays as the note.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE qualification_tokens AS
                SELECT t."PersonId", t."SchoolId", btrim(x.token) AS token, x.n,
                    (SELECT s."Id" FROM "Subjects" s
                     WHERE s."SchoolId" = t."SchoolId"
                         AND (lower(s."Abbreviation") = lower(btrim(x.token)) OR lower(s."Name") = lower(btrim(x.token)))
                     ORDER BY s."Id" LIMIT 1) AS subject_id
                FROM "TeacherProfiles" t,
                    LATERAL regexp_split_to_table(t."QualificationNote", '[,;/]') WITH ORDINALITY AS x(token, n)
                WHERE t."QualificationNote" IS NOT NULL AND btrim(x.token) <> '';

                INSERT INTO "TeacherQualifications" ("SchoolId", "TeacherProfileId", "SubjectId")
                SELECT DISTINCT "SchoolId", "PersonId", subject_id FROM qualification_tokens WHERE subject_id IS NOT NULL;

                UPDATE "TeacherProfiles" t
                SET "QualificationNote" = (
                    SELECT string_agg(q.token, ', ' ORDER BY q.n) FROM qualification_tokens q
                    WHERE q."PersonId" = t."PersonId" AND q.subject_id IS NULL)
                WHERE t."QualificationNote" IS NOT NULL;

                DROP TABLE qualification_tokens;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClassCurriculumItems");

            migrationBuilder.DropTable(
                name: "CurriculumItems");

            migrationBuilder.DropTable(
                name: "TeacherQualifications");

            migrationBuilder.DropTable(
                name: "TeachingAssignments");

            migrationBuilder.DropColumn(
                name: "ContractedHours",
                table: "TeacherProfiles");

            migrationBuilder.DropColumn(
                name: "TeachingDays",
                table: "TeacherProfiles");

            migrationBuilder.RenameColumn(
                name: "QualificationNote",
                table: "TeacherProfiles",
                newName: "Qualification");
        }
    }
}
