using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <summary>A deleted grade, absence, homework, event or conversation
    /// takes its notifications along, however it goes — on its own, with its
    /// pupil, subject or class, or in bulk. The trigger's arguments are the
    /// notification kinds about that table's rows.</summary>
    public partial class NotificationsOfDeletedThings : Migration
    {
        private static readonly (string Table, string Types)[] Things =
        [
            ("Grades", "'Grade'"),
            ("Absences", "'Absence', 'Excuse'"),
            ("Homework", "'Homework'"),
            ("Events", "'Event'"),
            ("Conversations", "'Message'"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TargetId",
                table: "Notifications",
                column: "TargetId");

            migrationBuilder.Sql("""
                CREATE FUNCTION notifications_of_deleted() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    DELETE FROM "Notifications" n
                    USING deleted d
                    WHERE n."TargetId" = d."Id" AND n."SchoolId" = d."SchoolId" AND n."Type" = ANY (TG_ARGV);
                    RETURN NULL;
                END $$;
                """);
            foreach (var (table, types) in Things)
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER "{table}_notifications" AFTER DELETE ON "{table}"
                    REFERENCING OLD TABLE AS deleted
                    FOR EACH STATEMENT EXECUTE FUNCTION notifications_of_deleted({types});
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, _) in Things)
            {
                migrationBuilder.Sql($"""DROP TRIGGER "{table}_notifications" ON "{table}";""");
            }
            migrationBuilder.Sql("DROP FUNCTION notifications_of_deleted();");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_TargetId",
                table: "Notifications");
        }
    }
}
