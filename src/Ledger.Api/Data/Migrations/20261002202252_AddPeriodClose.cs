using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ledger.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LedgerState",
                columns: table => new
                {
                    Id = table.Column<byte>(type: "tinyint", nullable: false),
                    ClosedThrough = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerState", x => x.Id);
                    table.CheckConstraint("CK_LedgerState_Singleton", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "PeriodCloses",
                columns: table => new
                {
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeriodCloses", x => x.PeriodEnd);
                });

            migrationBuilder.CreateTable(
                name: "PeriodBalances",
                columns: table => new
                {
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Debits = table.Column<decimal>(type: "decimal(28,4)", precision: 28, scale: 4, nullable: false),
                    Credits = table.Column<decimal>(type: "decimal(28,4)", precision: 28, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeriodBalances", x => new { x.PeriodEnd, x.AccountId });
                    table.ForeignKey(
                        name: "FK_PeriodBalances_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PeriodBalances_PeriodCloses_PeriodEnd",
                        column: x => x.PeriodEnd,
                        principalTable: "PeriodCloses",
                        principalColumn: "PeriodEnd",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "LedgerState",
                columns: new[] { "Id", "ClosedThrough" },
                values: new object[] { (byte)1, null });

            migrationBuilder.CreateIndex(
                name: "IX_PeriodBalances_AccountId",
                table: "PeriodBalances",
                column: "AccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LedgerState");

            migrationBuilder.DropTable(
                name: "PeriodBalances");

            migrationBuilder.DropTable(
                name: "PeriodCloses");
        }
    }
}
