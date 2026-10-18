using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sova.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserInvitationToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InvitationToken",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InvitationTokenExpiresAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvitationToken",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "InvitationTokenExpiresAt",
                table: "Users");
        }
    }
}
