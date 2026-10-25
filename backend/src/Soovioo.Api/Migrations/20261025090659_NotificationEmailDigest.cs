using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class NotificationEmailDigest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DigestSentAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "DigestTime",
                table: "Users",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailUnsubscribeToken",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "MutedEmailNotifications",
                table: "Users",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "DigestTime",
                table: "Schools",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(17, 0, 0));

            migrationBuilder.AddColumn<string[]>(
                name: "ForcedEmailNotifications",
                table: "Schools",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<bool>(
                name: "InApp",
                table: "Notifications",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.UpdateData(
                table: "Schools",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DigestTime", "ForcedEmailNotifications" },
                values: new object[] { new TimeOnly(17, 0, 0), new string[0] });

            migrationBuilder.CreateIndex(
                name: "IX_Users_EmailUnsubscribeToken",
                table: "Users",
                column: "EmailUnsubscribeToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_EmailUnsubscribeToken",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DigestSentAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DigestTime",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailUnsubscribeToken",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MutedEmailNotifications",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DigestTime",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "ForcedEmailNotifications",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "InApp",
                table: "Notifications");
        }
    }
}
