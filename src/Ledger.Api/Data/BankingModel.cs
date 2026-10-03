using Ledger.Domain;
using Ledger.Domain.Banking;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Api.Data;

internal static class BankingModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BankStatement>(statement =>
        {
            statement.HasKey(s => s.Id);
            statement.Property(s => s.Id).ValueGeneratedNever();
            statement.Property(s => s.ContentHash).HasMaxLength(BankStatement.HashLength).IsFixedLength().IsUnicode(false);
            statement.Property(s => s.Status).HasConversion<byte>();
            statement.Property(s => s.ImportedBy).HasMaxLength(Actor.MaxLength);
            statement.Property(s => s.Error).HasMaxLength(BankStatement.MaxErrorLength);
            statement.HasIndex(s => new { s.AccountId, s.ContentHash }).IsUnique();
            statement.HasIndex(s => s.Status);
            statement.HasOne<Account>().WithMany().HasForeignKey(s => s.AccountId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BankLine>(line =>
        {
            line.HasKey(l => l.Id);
            line.Property(l => l.Id).ValueGeneratedNever();
            line.Property(l => l.Reference).HasMaxLength(StatementParser.MaxReferenceLength);
            line.Property(l => l.Description).HasMaxLength(StatementParser.MaxDescriptionLength);
            line.Property(l => l.Amount).HasPrecision(19, JournalEntry.AmountScale);
            line.Property(l => l.MatchedBy).HasMaxLength(Actor.MaxLength);
            line.Property<byte[]>(ReceivablesModel.RowVersion).IsRowVersion();
            line.Ignore(l => l.IsMatched);
            line.HasIndex(l => new { l.AccountId, l.Reference }).IsUnique();
            line.HasIndex(l => new { l.MatchedEntryId, l.MatchedLineNumber }).IsUnique();
            line.HasOne<BankStatement>().WithMany().HasForeignKey(l => l.StatementId).OnDelete(DeleteBehavior.Restrict);
            line.HasOne<Account>().WithMany().HasForeignKey(l => l.AccountId).OnDelete(DeleteBehavior.Restrict);
            line.HasOne<JournalLine>()
                .WithMany()
                .HasForeignKey(l => new { l.MatchedEntryId, l.MatchedLineNumber })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
