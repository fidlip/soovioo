using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sova.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEventColor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "Events",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Color",
                table: "Events");
        }
    }
}
