using Ledger.Api.Auth;
using Ledger.Domain;
using Ledger.Domain.Receivables;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Api.Data;

public sealed class LedgerDbContext(DbContextOptions<LedgerDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();

    public DbSet<JournalLine> JournalLines => Set<JournalLine>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<LedgerState> LedgerStates => Set<LedgerState>();

    public DbSet<PeriodClose> PeriodCloses => Set<PeriodClose>();

    public DbSet<PeriodBalance> PeriodBalances => Set<PeriodBalance>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();

    public DbSet<NumberSequence> Sequences => Set<NumberSequence>();

    public DbSet<AppUser> Users => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ReceivablesModel.Configure(modelBuilder);

        modelBuilder.Entity<AppUser>(user =>
        {
            user.ToTable("Users");
            user.HasKey(u => u.Id);
            user.Property(u => u.Id).ValueGeneratedNever();
            user.Property(u => u.UserName).HasMaxLength(AppUser.MaxUserNameLength).IsUnicode(false);
            user.Property(u => u.PasswordHash).HasMaxLength(256).IsUnicode(false);
            user.Property(u => u.Roles).HasConversion<int>();
            user.HasIndex(u => u.UserName).IsUnique();
        });

        modelBuilder.Entity<LedgerState>(state =>
        {
            state.HasKey(s => s.Id);
            state.Property(s => s.Id).ValueGeneratedNever();
            state.ToTable("LedgerState", table => table.HasCheckConstraint("CK_LedgerState_Singleton", "[Id] = 1"));
            state.HasData(new LedgerState());
        });

        modelBuilder.Entity<PeriodClose>(close =>
        {
            close.HasKey(c => c.PeriodEnd);
            close.Property(c => c.ClosedBy).HasMaxLength(Actor.MaxLength);
        });

        modelBuilder.Entity<PeriodBalance>(balance =>
        {
            balance.HasKey(b => new { b.PeriodEnd, b.AccountId });
            balance.Property(b => b.Debits).HasPrecision(28, JournalEntry.AmountScale);
            balance.Property(b => b.Credits).HasPrecision(28, JournalEntry.AmountScale);
            balance.HasOne<PeriodClose>()
                .WithMany()
                .HasForeignKey(b => b.PeriodEnd)
                .OnDelete(DeleteBehavior.Restrict);
            balance.HasOne<Account>()
                .WithMany()
                .HasForeignKey(b => b.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

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
            entry.Property(e => e.PostedBy).HasMaxLength(Actor.MaxLength);
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
