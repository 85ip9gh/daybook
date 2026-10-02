using Ledger.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Api.Data;

public sealed class LedgerDbContext(DbContextOptions<LedgerDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();

    public DbSet<JournalLine> JournalLines => Set<JournalLine>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(account =>
        {
            account.HasKey(a => a.Id);
            account.Property(a => a.Id).ValueGeneratedNever();
            account.Property(a => a.Code).HasMaxLength(Account.MaxCodeLength);
            account.Property(a => a.Name).HasMaxLength(Account.MaxNameLength);
            account.Property(a => a.Type).HasConversion<byte>();
            account.Property(a => a.Currency).HasMaxLength(3).IsFixedLength().IsUnicode(false);
            account.HasIndex(a => a.Code).IsUnique();
        });

        modelBuilder.Entity<JournalEntry>(entry =>
        {
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).ValueGeneratedNever();
            entry.Property(e => e.Description).HasMaxLength(JournalEntry.MaxDescriptionLength);
            entry.Property(e => e.Currency).HasMaxLength(3).IsFixedLength().IsUnicode(false);
            entry.HasIndex(e => e.EntryDate);
            entry.HasOne<JournalEntry>()
                .WithOne()
                .HasForeignKey<JournalEntry>(e => e.ReversesEntryId)
                .OnDelete(DeleteBehavior.Restrict);
            entry.HasMany(e => e.Lines)
                .WithOne()
                .HasForeignKey(l => l.JournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);
            entry.Navigation(e => e.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<JournalLine>(line =>
        {
            line.HasKey(l => new { l.JournalEntryId, l.LineNumber });
            line.Property(l => l.Side).HasConversion<byte>();
            line.Property(l => l.Amount).HasPrecision(19, JournalEntry.AmountScale);
            line.HasOne<Account>()
                .WithMany()
                .HasForeignKey(l => l.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
            line.HasIndex(l => l.AccountId);
            line.ToTable(table =>
            {
                table.HasCheckConstraint("CK_JournalLines_Amount_Positive", "[Amount] > 0");
                table.HasCheckConstraint("CK_JournalLines_Side_Known", "[Side] IN (1, 2)");
            });
        });

        modelBuilder.Entity<IdempotencyRecord>(record =>
        {
            record.HasKey(r => r.Key);
            record.Property(r => r.Key).HasMaxLength(IdempotencyRecord.MaxKeyLength);
            record.Property(r => r.RequestHash).HasMaxLength(IdempotencyRecord.HashLength).IsFixedLength().IsUnicode(false);
            record.HasOne<JournalEntry>()
                .WithOne()
                .HasForeignKey<IdempotencyRecord>(r => r.JournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
