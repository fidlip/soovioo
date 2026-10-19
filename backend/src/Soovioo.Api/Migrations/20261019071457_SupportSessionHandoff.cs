using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class SupportSessionHandoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "SupportSessions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "HandoffCodeHash",
                table: "SupportSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HandoffExpiresAt",
                table: "SupportSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefreshSecretHash",
                table: "SupportSessions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "SupportSessions");

            migrationBuilder.DropColumn(
                name: "HandoffCodeHash",
                table: "SupportSessions");

            migrationBuilder.DropColumn(
                name: "HandoffExpiresAt",
                table: "SupportSessions");

            migrationBuilder.DropColumn(
                name: "RefreshSecretHash",
                table: "SupportSessions");
        }
    }
}
