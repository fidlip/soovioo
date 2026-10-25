using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class NotificationPush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "MutedPushNotifications",
                table: "Users",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PushFullTextAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "PushQuietFrom",
                table: "Users",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PushQuietOff",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "PushQuietTo",
                table: "Users",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PushFullText",
                table: "Schools",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "PushQuietFrom",
                table: "Schools",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(21, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "PushQuietTo",
                table: "Schools",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(7, 0, 0));

            migrationBuilder.AddColumn<bool>(
                name: "Push",
                table: "Notifications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PushedAt",
                table: "Notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PushDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Token = table.Column<string>(type: "text", nullable: false),
                    Platform = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PushDevices_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Schools",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "PushFullText", "PushQuietFrom", "PushQuietTo" },
                values: new object[] { false, new TimeOnly(21, 0, 0), new TimeOnly(7, 0, 0) });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_PushPending",
                table: "Notifications",
                column: "UserId",
                filter: "\"Push\" AND \"PushedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PushDevices_Token",
                table: "PushDevices",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushDevices_UserId",
                table: "PushDevices",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PushDevices");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_PushPending",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "MutedPushNotifications",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PushFullTextAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PushQuietFrom",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PushQuietOff",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PushQuietTo",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PushFullText",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "PushQuietFrom",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "PushQuietTo",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "Push",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "PushedAt",
                table: "Notifications");
        }
    }
}
