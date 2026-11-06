using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class TimetableUpdateMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Mismatches",
                table: "TimetableGenerations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "FollowPlan");

            migrationBuilder.AddColumn<string>(
                name: "Moves",
                table: "TimetableGenerations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Fewest");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Mismatches",
                table: "TimetableGenerations");

            migrationBuilder.DropColumn(
                name: "Moves",
                table: "TimetableGenerations");
        }
    }
}
