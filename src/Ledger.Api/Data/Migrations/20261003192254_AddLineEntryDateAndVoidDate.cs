using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ledger.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLineEntryDateAndVoidDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JournalLines_AccountId",
                table: "JournalLines");

            migrationBuilder.AddColumn<DateOnly>(
                name: "EntryDate",
                table: "JournalLines",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.Sql("UPDATE line SET EntryDate = entry.EntryDate FROM JournalLines AS line JOIN JournalEntries AS entry ON entry.Id = line.JournalEntryId;");
            migrationBuilder.Sql("CREATE UNIQUE INDEX UX_JournalEntries_Id_EntryDate ON JournalEntries (Id, EntryDate);");
            migrationBuilder.Sql("ALTER TABLE JournalLines ADD CONSTRAINT FK_JournalLines_JournalEntries_EntryDate FOREIGN KEY (JournalEntryId, EntryDate) REFERENCES JournalEntries (Id, EntryDate);");

            migrationBuilder.AddColumn<DateOnly>(
                name: "VoidedOn",
                table: "Invoices",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_AccountId_EntryDate",
                table: "JournalLines",
                columns: new[] { "AccountId", "EntryDate" })
                .Annotation("SqlServer:Include", new[] { "Side", "Amount" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE JournalLines DROP CONSTRAINT FK_JournalLines_JournalEntries_EntryDate;");
            migrationBuilder.Sql("DROP INDEX UX_JournalEntries_Id_EntryDate ON JournalEntries;");

            migrationBuilder.DropIndex(
                name: "IX_JournalLines_AccountId_EntryDate",
                table: "JournalLines");

            migrationBuilder.DropColumn(
                name: "EntryDate",
                table: "JournalLines");

            migrationBuilder.DropColumn(
                name: "VoidedOn",
                table: "Invoices");

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_AccountId",
                table: "JournalLines",
                column: "AccountId");
        }
    }
}
