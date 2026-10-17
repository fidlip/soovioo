using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sova.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultVariableSymbol",
                table: "Students",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountNumber",
                table: "Schools",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AmountType = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Recurrence = table.Column<int>(type: "integer", nullable: false),
                    RecurrenceEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NextOccurrenceDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    Audience = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentParents",
                columns: table => new
                {
                    PaymentId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentParents", x => new { x.PaymentId, x.UserId });
                    table.ForeignKey(
                        name: "FK_PaymentParents_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentParents_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentParents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentRecipients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PaymentId = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    VariableSymbol = table.Column<string>(type: "text", nullable: true),
                    InstallmentDueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Paid = table.Column<bool>(type: "boolean", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentRecipients_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentRecipients_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentRecipients_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentSchoolClasses",
                columns: table => new
                {
                    PaymentId = table.Column<int>(type: "integer", nullable: false),
                    SchoolClassId = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentSchoolClasses", x => new { x.PaymentId, x.SchoolClassId });
                    table.ForeignKey(
                        name: "FK_PaymentSchoolClasses_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentSchoolClasses_SchoolClasses_SchoolClassId",
                        column: x => x.SchoolClassId,
                        principalTable: "SchoolClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentSchoolClasses_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentStudents",
                columns: table => new
                {
                    PaymentId = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentStudents", x => new { x.PaymentId, x.StudentId });
                    table.ForeignKey(
                        name: "FK_PaymentStudents_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentStudents_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentStudents_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Schools",
                keyColumn: "Id",
                keyValue: 1,
                column: "BankAccountNumber",
                value: null);

            migrationBuilder.UpdateData(
                table: "Students",
                keyColumn: "Id",
                keyValue: 1,
                column: "DefaultVariableSymbol",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentParents_SchoolId",
                table: "PaymentParents",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentParents_UserId",
                table: "PaymentParents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecipients_PaymentId",
                table: "PaymentRecipients",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecipients_SchoolId",
                table: "PaymentRecipients",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecipients_StudentId",
                table: "PaymentRecipients",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_SchoolId",
                table: "Payments",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSchoolClasses_SchoolClassId",
                table: "PaymentSchoolClasses",
                column: "SchoolClassId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentSchoolClasses_SchoolId",
                table: "PaymentSchoolClasses",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentStudents_SchoolId",
                table: "PaymentStudents",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentStudents_StudentId",
                table: "PaymentStudents",
                column: "StudentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentParents");

            migrationBuilder.DropTable(
                name: "PaymentRecipients");

            migrationBuilder.DropTable(
                name: "PaymentSchoolClasses");

            migrationBuilder.DropTable(
                name: "PaymentStudents");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropColumn(
                name: "DefaultVariableSymbol",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "BankAccountNumber",
                table: "Schools");
        }
    }
}
