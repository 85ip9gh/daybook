using Ledger.Domain.Receivables;

namespace Ledger.Domain.Tests;

public class ActorTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    private readonly Account _receivable = Account.Open("1200", "Accounts receivable", AccountType.Asset, "CAD");
    private readonly Account _revenue = Account.Open("4000", "Revenue", AccountType.Revenue, "CAD");
    private readonly Account _bank = Account.Open("1000", "Bank", AccountType.Asset, "CAD");

    private Dictionary<Guid, Account> Accounts => new[] { _receivable, _revenue, _bank }.ToDictionary(account => account.Id);

    private JournalEntry Post(string? postedBy) =>
        JournalEntry.Post(
            Today,
            "Cash sale",
            "CAD",
            [new LineDraft(_bank.Id, EntrySide.Debit, 10m), new LineDraft(_revenue.Id, EntrySide.Credit, 10m)],
            Accounts,
            null,
            postedBy,
            DateTimeOffset.UnixEpoch);

    private Invoice Draft(string? draftedBy) =>
        Invoice.Draft(Guid.NewGuid(), "CAD", _receivable.Id, [new InvoiceLineDraft("Work", 1m, 100m, _revenue.Id)], null, Accounts, draftedBy);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_journal_entry_cannot_be_posted_by_nobody(string? postedBy)
    {
        var exception = Assert.Throws<LedgerRuleException>(() => Post(postedBy));

        Assert.Equal("actor_required", exception.Code);
    }

    [Fact]
    public void An_entry_and_its_reversal_each_record_who_posted_them()
    {
        var entry = Post("amira");

        var reversal = entry.Reverse(Today, null, "jonas", DateTimeOffset.UnixEpoch);

        Assert.Equal("amira", entry.PostedBy);
        Assert.Equal("jonas", reversal.PostedBy);
    }

    [Theory]
    [InlineData("amira")]
    [InlineData("AMIRA")]
    [InlineData(" amira ")]
    public void Whoever_drafted_an_invoice_cannot_issue_it(string issuer)
    {
        var invoice = Draft("amira");

        Assert.Throws<SegregationOfDutiesException>(() => invoice.Issue(1, Today, Today, issuer));
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Null(invoice.Number);
    }

    [Fact]
    public void An_invoice_records_who_drafted_issued_and_voided_it()
    {
        var invoice = Draft("amira");

        invoice.Issue(1, Today, Today, "jonas");
        invoice.Void("priya", new DateOnly(2026, 10, 2));

        Assert.Equal(("amira", "jonas", "priya"), (invoice.DraftedBy, invoice.IssuedBy, invoice.VoidedBy));
    }

    [Fact]
    public void A_payment_records_who_received_it()
    {
        var invoice = Draft("amira");
        invoice.Issue(1, Today, Today, "jonas");

        var received = Payment.Receive(
            invoice.CustomerId,
            Today,
            100m,
            "CAD",
            _bank.Id,
            [new AllocationDraft(invoice.Id, 100m)],
            new[] { invoice }.ToDictionary(invoice => invoice.Id),
            "amira");

        Assert.Equal("amira", received.Payment.ReceivedBy);
    }
}
