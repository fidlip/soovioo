using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class DeleteArchivedTimetableVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "TimetableVersions" v SET "Status" = 'Draft', "PublishedAt" = NULL, "PublishedByUserId" = NULL
                WHERE v."Status" = 'Archived' AND EXISTS (
                    SELECT 1 FROM "Homework" h JOIN "StudentGroups" g ON g."Id" = h."StudentGroupId"
                    WHERE g."TimetableVersionId" = v."Id");
                DELETE FROM "TimetableVersions" WHERE "Status" = 'Archived';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
