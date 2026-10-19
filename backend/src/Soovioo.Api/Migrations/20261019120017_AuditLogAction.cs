using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AuditLogAction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Action",
                table: "AuditLogEntries",
                type: "text",
                nullable: true);

            // Backfill from the descriptions written so far: the longest
            // known action the description starts with; logins by hand;
            // the rest as method + path.
            migrationBuilder.Sql("""
                UPDATE "AuditLogEntries" SET "Action" = 'Neúspěšné přihlášení'
                WHERE "Description" LIKE 'Neúspěšné přihlášení%';
                """);
            foreach (var action in Audit.AuditDescriptions.AllActions.OrderByDescending(a => a.Length))
            {
                var literal = action.Replace("'", "''");
                migrationBuilder.Sql($"""
                    UPDATE "AuditLogEntries" SET "Action" = '{literal}'
                    WHERE "Action" IS NULL AND ("Description" = '{literal}' OR "Description" LIKE '{literal} %');
                    """);
            }
            migrationBuilder.Sql("""
                UPDATE "AuditLogEntries" SET "Action" = "Method" || ' ' || "Path" WHERE "Action" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Action",
                table: "AuditLogEntries");
        }
    }
}
