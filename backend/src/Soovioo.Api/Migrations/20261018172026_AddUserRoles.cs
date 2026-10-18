using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int[]>(
                name: "Roles",
                table: "Users",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            // Every existing account had exactly one role — it becomes both
            // the active one (Role) and the whole set.
            migrationBuilder.Sql("UPDATE \"Users\" SET \"Roles\" = ARRAY[\"Role\"];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Roles",
                table: "Users");
        }
    }
}
