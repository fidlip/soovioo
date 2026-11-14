using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Soovioo.SupportPlans.Migrations
{
    /// <inheritdoc />
    public partial class InitSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Plans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResponseWorkingDays = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Id);
                    table.CheckConstraint("CK_Plans_ResponseWorkingDays", "\"ResponseWorkingDays\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "SchoolPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    PlanId = table.Column<int>(type: "integer", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchoolPlans", x => x.Id);
                    table.CheckConstraint("CK_SchoolPlans_Validity", "\"ValidTo\" IS NULL OR \"ValidTo\" >= \"ValidFrom\"");
                    table.ForeignKey(
                        name: "FK_SchoolPlans_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Plans",
                columns: new[] { "Id", "Code", "Name", "ResponseWorkingDays" },
                values: new object[,]
                {
                    { 1, "Free", "Free", 5 },
                    { 2, "Paid", "Placený", 2 },
                    { 3, "Priority", "Prioritní", 1 },
                    { 4, "Demo", "Demo", 5 },
                    { 5, "Pilot", "Pilot", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Plans_Code",
                table: "Plans",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchoolPlans_PlanId",
                table: "SchoolPlans",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_SchoolPlans_SchoolId_ValidFrom",
                table: "SchoolPlans",
                columns: new[] { "SchoolId", "ValidFrom" });

            migrationBuilder.Sql(
                "SELECT setval(pg_get_serial_sequence('\"Plans\"', 'Id'), (SELECT MAX(\"Id\") FROM \"Plans\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchoolPlans");

            migrationBuilder.DropTable(
                name: "Plans");
        }
    }
}
