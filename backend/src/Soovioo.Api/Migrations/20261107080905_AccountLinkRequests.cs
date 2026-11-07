using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Soovioo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AccountLinkRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountLinkRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CodeHash = table.Column<string>(type: "text", nullable: false),
                    InitiatorUserId = table.Column<int>(type: "integer", nullable: false),
                    TargetSchoolId = table.Column<int>(type: "integer", nullable: false),
                    ApprovedUserId = table.Column<int>(type: "integer", nullable: true),
                    CompletionCodeHash = table.Column<string>(type: "text", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountLinkRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountLinkRequests_Schools_TargetSchoolId",
                        column: x => x.TargetSchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccountLinkRequests_Users_InitiatorUserId",
                        column: x => x.InitiatorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountLinkRequests_CodeHash",
                table: "AccountLinkRequests",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountLinkRequests_CompletionCodeHash",
                table: "AccountLinkRequests",
                column: "CompletionCodeHash",
                unique: true,
                filter: "\"CompletionCodeHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccountLinkRequests_InitiatorUserId",
                table: "AccountLinkRequests",
                column: "InitiatorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountLinkRequests_TargetSchoolId",
                table: "AccountLinkRequests",
                column: "TargetSchoolId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountLinkRequests");
        }
    }
}
