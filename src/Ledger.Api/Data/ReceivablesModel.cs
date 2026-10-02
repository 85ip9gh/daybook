using Ledger.Domain;
using Ledger.Domain.Receivables;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Api.Data;

public sealed class NumberSequence
{
    public const string Invoice = "invoice";
    public const int MaxNameLength = 50;

    public required string Name { get; init; }

    public int LastValue { get; init; }
}

internal static class ReceivablesModel
{
    public const string RowVersion = "RowVersion";

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NumberSequence>(sequence =>
        {
            sequence.ToTable("Sequences");
            sequence.HasKey(s => s.Name);
            sequence.Property(s => s.Name).HasMaxLength(NumberSequence.MaxNameLength).IsUnicode(false);
            sequence.HasData(new NumberSequence { Name = NumberSequence.Invoice, LastValue = 0 });
        });

        modelBuilder.Entity<Customer>(customer =>
        {
            customer.HasKey(c => c.Id);
            customer.Property(c => c.Id).ValueGeneratedNever();
            customer.Property(c => c.Code).HasMaxLength(Customer.MaxCodeLength);
            customer.Property(c => c.Name).HasMaxLength(Customer.MaxNameLength);
            customer.HasIndex(c => c.Code).IsUnique();
        });

        modelBuilder.Entity<Invoice>(invoice =>
        {
            invoice.HasKey(i => i.Id);
            invoice.Property(i => i.Id).ValueGeneratedNever();
            invoice.Property(i => i.Currency).HasMaxLength(3).IsFixedLength().IsUnicode(false);
            invoice.Property(i => i.Status).HasConversion<byte>();
            invoice.Property(i => i.TaxRate).HasPrecision(9, 6);
            invoice.Property(i => i.Subtotal).HasPrecision(19, JournalEntry.AmountScale);
            invoice.Property(i => i.Tax).HasPrecision(19, JournalEntry.AmountScale);
            invoice.Property(i => i.Total).HasPrecision(19, JournalEntry.AmountScale);
            invoice.Property(i => i.AmountPaid).HasPrecision(19, JournalEntry.AmountScale);
            invoice.Property<byte[]>(RowVersion).IsRowVersion();
            invoice.Ignore(i => i.Outstanding);
            invoice.HasIndex(i => i.Number).IsUnique();
            invoice.HasIndex(i => new { i.CustomerId, i.Status });
            invoice.HasOne<Customer>().WithMany().HasForeignKey(i => i.CustomerId).OnDelete(DeleteBehavior.Restrict);
            invoice.HasOne<Account>().WithMany().HasForeignKey(i => i.ReceivableAccountId).OnDelete(DeleteBehavior.Restrict);
            invoice.HasOne<Account>().WithMany().HasForeignKey(i => i.TaxAccountId).OnDelete(DeleteBehavior.Restrict);
            invoice.HasOne<JournalEntry>().WithOne().HasForeignKey<Invoice>(i => i.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
            invoice.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            invoice.Navigation(i => i.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
            invoice.ToTable(table => table.HasCheckConstraint("CK_Invoices_AmountPaid_Within_Total", "[AmountPaid] >= 0 AND [AmountPaid] <= [Total]"));
        });

        modelBuilder.Entity<InvoiceLine>(line =>
        {
            line.HasKey(l => new { l.InvoiceId, l.LineNumber });
            line.Property(l => l.Description).HasMaxLength(InvoiceLine.MaxDescriptionLength);
            line.Property(l => l.Quantity).HasPrecision(18, Invoice.QuantityScale);
            line.Property(l => l.UnitPrice).HasPrecision(19, JournalEntry.AmountScale);
            line.Property(l => l.Amount).HasPrecision(19, JournalEntry.AmountScale);
            line.HasOne<Account>().WithMany().HasForeignKey(l => l.RevenueAccountId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Payment>(payment =>
        {
            payment.HasKey(p => p.Id);
            payment.Property(p => p.Id).ValueGeneratedNever();
            payment.Property(p => p.Currency).HasMaxLength(3).IsFixedLength().IsUnicode(false);
            payment.Property(p => p.Amount).HasPrecision(19, JournalEntry.AmountScale);
            payment.HasOne<Customer>().WithMany().HasForeignKey(p => p.CustomerId).OnDelete(DeleteBehavior.Restrict);
            payment.HasOne<Account>().WithMany().HasForeignKey(p => p.DepositAccountId).OnDelete(DeleteBehavior.Restrict);
            payment.HasOne<JournalEntry>().WithOne().HasForeignKey<Payment>(p => p.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
            payment.HasMany(p => p.Allocations).WithOne().HasForeignKey(a => a.PaymentId).OnDelete(DeleteBehavior.Restrict);
            payment.Navigation(p => p.Allocations).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<PaymentAllocation>(allocation =>
        {
            allocation.HasKey(a => new { a.PaymentId, a.InvoiceId });
            allocation.Property(a => a.Amount).HasPrecision(19, JournalEntry.AmountScale);
            allocation.HasOne<Invoice>().WithMany().HasForeignKey(a => a.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            allocation.ToTable(table => table.HasCheckConstraint("CK_PaymentAllocations_Amount_Positive", "[Amount] > 0"));
        });
    }
}
