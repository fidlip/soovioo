using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReserveTestSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // test.{MainDomain} is the test instance: a school there would be
            // unreachable, so it moves aside.
            migrationBuilder.Sql("""
                UPDATE "Schools" SET "Slug" = 'test-bak'
                WHERE lower("Slug") = 'test'
                  AND NOT EXISTS (SELECT 1 FROM "Schools" WHERE "Slug" = 'test-bak');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
