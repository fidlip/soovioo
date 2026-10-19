using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class PastelSubjectColors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Saturated subject palette (Tailwind 500) -> pastel (Tailwind 200),
            // so timetable cell text stays readable. Custom colors untouched.
            migrationBuilder.Sql("UPDATE \"Subjects\" SET \"Color\" = '#c7d2fe' WHERE lower(\"Color\") = '#6366f1';");
            migrationBuilder.Sql("UPDATE \"Subjects\" SET \"Color\" = '#fecaca' WHERE lower(\"Color\") = '#ef4444';");
            migrationBuilder.Sql("UPDATE \"Subjects\" SET \"Color\" = '#fed7aa' WHERE lower(\"Color\") = '#f97316';");
            migrationBuilder.Sql("UPDATE \"Subjects\" SET \"Color\" = '#fef08a' WHERE lower(\"Color\") = '#eab308';");
            migrationBuilder.Sql("UPDATE \"Subjects\" SET \"Color\" = '#bbf7d0' WHERE lower(\"Color\") = '#22c55e';");
            migrationBuilder.Sql("UPDATE \"Subjects\" SET \"Color\" = '#99f6e4' WHERE lower(\"Color\") = '#14b8a6';");
            migrationBuilder.Sql("UPDATE \"Subjects\" SET \"Color\" = '#bfdbfe' WHERE lower(\"Color\") = '#3b82f6';");
            migrationBuilder.Sql("UPDATE \"Subjects\" SET \"Color\" = '#e9d5ff' WHERE lower(\"Color\") = '#a855f7';");
            migrationBuilder.Sql("UPDATE \"Subjects\" SET \"Color\" = '#fbcfe8' WHERE lower(\"Color\") = '#ec4899';");
            migrationBuilder.Sql("UPDATE \"Subjects\" SET \"Color\" = '#e2e8f0' WHERE lower(\"Color\") = '#64748b';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Subjects\" SET \"Color\" = '#6366f1' WHERE lower(\"Color\") = '#c7d2fe';");
            migrationBuilder.Sql("UPDATE \"Subjects\" SET \"Color\" = '#ef4444' WHERE lower(\"Color\") = '#fecaca';");
            migrationBuilder.Sql("UPDATE \"Subjects\" SET \"Color\" = '#f97316' WHERE lower(\"Color\") = '#fed7aa';");
            migrationBuilder.Sql("UPDATE \"Subjects\" SET \"Color\" = '#eab308' WHERE lower(\"Color\") = '#fef08a';");
            migrationBuilder.Sql("UPDATE \"Subjects\" SET \"Color\" = '#22c55e' WHERE lower(\"Color\") = '#bbf7d0';");
            migrationBuilder.Sql("UPDATE \"Subjects\" SET \"Color\" = '#14b8a6' WHERE lower(\"Color\") = '#99f6e4';");
            migrationBuilder.Sql("UPDATE \"Subjects\" SET \"Color\" = '#3b82f6' WHERE lower(\"Color\") = '#bfdbfe';");
            migrationBuilder.Sql("UPDATE \"Subjects\" SET \"Color\" = '#a855f7' WHERE lower(\"Color\") = '#e9d5ff';");
            migrationBuilder.Sql("UPDATE \"Subjects\" SET \"Color\" = '#ec4899' WHERE lower(\"Color\") = '#fbcfe8';");
            migrationBuilder.Sql("UPDATE \"Subjects\" SET \"Color\" = '#64748b' WHERE lower(\"Color\") = '#e2e8f0';");
        }
    }
}
