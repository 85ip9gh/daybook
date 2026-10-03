using Ledger.Domain.Receivables;

namespace Ledger.Domain.Tests;

public class ReceivablesTests
{
    private static readonly DateOnly IssueDate = new(2026, 10, 1);
    private static readonly Guid CustomerId = Guid.NewGuid();

    private readonly Account _receivable = Account.Open("1200", "Accounts receivable", AccountType.Asset, "CAD");
    private readonly Account _services = Account.Open("4000", "Services", AccountType.Revenue, "CAD");
    private readonly Account _licences = Account.Open("4100", "Licences", AccountType.Revenue, "CAD");
    private readonly Account _tax = Account.Open("2100", "Sales tax payable", AccountType.Liability, "CAD");
    private readonly Account _bank = Account.Open("1000", "Bank", AccountType.Asset, "CAD");

    private Dictionary<Guid, Account> Accounts => new[] { _receivable, _services, _licences, _tax, _bank }.ToDictionary(account => account.Id);

    private Invoice Draft(TaxDraft? tax, params InvoiceLineDraft[] lines) =>
        Invoice.Draft(CustomerId, "CAD", _receivable.Id, lines, tax, Accounts);

    private Invoice Issued(decimal amount, int number = 1)
    {
        var invoice = Draft(null, new InvoiceLineDraft("Work", 1m, amount, _services.Id));
        invoice.Issue(number, IssueDate, IssueDate.AddDays(30));
        return invoice;
    }

    private static string CodeOf(Action act) => Assert.Throws<LedgerRuleException>(act).Code;

    [Theory]
    [InlineData("0.025", "0.03")]
    [InlineData("0.035", "0.04")]
    [InlineData("0.034", "0.03")]
    public void Money_rounds_half_away_from_zero_not_to_even(string amount, string expected)
    {
        Assert.Equal(decimal.Parse(expected), Money.Round(decimal.Parse(amount)));
    }

    [Fact]
    public void A_draft_totals_its_lines_and_rounds_tax_once_on_the_subtotal()
    {
        var invoice = Draft(
            new TaxDraft(0.14m, _tax.Id),
            new InvoiceLineDraft("Hours", 3m, 19.99m, _services.Id),
            new InvoiceLineDraft("Seat", 1.5m, 0.33m, _licences.Id));

        Assert.Equal([59.97m, 0.50m], invoice.Lines.Select(line => line.Amount));
        Assert.Equal(60.47m, invoice.Subtotal);
        Assert.Equal(8.47m, invoice.Tax);
        Assert.Equal(68.94m, invoice.Total);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Equal(0m, invoice.Outstanding);
    }

    [Fact]
    public void A_draft_needs_a_line()
    {
        Assert.Equal("invoice_needs_a_line", CodeOf(() => Draft(null)));
    }

    [Theory]
    [InlineData(0, 10, "invoice_line_quantity_invalid")]
    [InlineData(1, 0, "invoice_line_price_invalid")]
    [InlineData(1, -5, "invoice_line_price_invalid")]
    public void A_line_needs_a_positive_quantity_and_price(int quantity, int price, string expected)
    {
        Assert.Equal(expected, CodeOf(() => Draft(null, new InvoiceLineDraft("Work", quantity, price, _services.Id))));
    }

    [Fact]
    public void A_line_that_rounds_to_nothing_is_rejected()
    {
        Assert.Equal("invoice_line_amount_zero", CodeOf(() => Draft(null, new InvoiceLineDraft("Work", 0.0001m, 0.01m, _services.Id))));
    }

    [Fact]
    public void Each_account_on_a_draft_must_play_its_role()
    {
        Assert.Equal("revenue_account_invalid", CodeOf(() => Draft(null, new InvoiceLineDraft("Work", 1m, 10m, _bank.Id))));
        Assert.Equal("tax_account_invalid", CodeOf(() => Draft(new TaxDraft(0.14m, _services.Id), new InvoiceLineDraft("Work", 1m, 10m, _services.Id))));
        Assert.Equal("receivable_account_invalid", CodeOf(() => Invoice.Draft(CustomerId, "CAD", _tax.Id, [new InvoiceLineDraft("Work", 1m, 10m, _services.Id)], null, Accounts)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_tax_rate_is_a_fraction(int rate)
    {
        Assert.Equal("tax_rate_invalid", CodeOf(() => Draft(new TaxDraft(rate, _tax.Id), new InvoiceLineDraft("Work", 1m, 10m, _services.Id))));
    }

    [Fact]
    public void Issuing_produces_a_balanced_posting_grouped_by_revenue_account()
    {
        var invoice = Draft(
            new TaxDraft(0.14m, _tax.Id),
            new InvoiceLineDraft("Hours", 2m, 50m, _services.Id),
            new InvoiceLineDraft("More hours", 1m, 50m, _services.Id),
            new InvoiceLineDraft("Seat", 1m, 30m, _licences.Id));

        var posting = invoice.Issue(7, IssueDate, IssueDate.AddDays(30));

        Assert.Equal(
            [
                new LineDraft(_receivable.Id, EntrySide.Debit, 205.20m),
                new LineDraft(_services.Id, EntrySide.Credit, 150m),
                new LineDraft(_licences.Id, EntrySide.Credit, 30m),
                new LineDraft(_tax.Id, EntrySide.Credit, 25.20m),
            ],
            posting);
        Assert.Equal((InvoiceStatus.Issued, 7, 205.20m), (invoice.Status, invoice.Number, invoice.Outstanding));
        JournalEntry.Post(IssueDate, "Invoice 7", "CAD", posting, Accounts, null, DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void An_invoice_is_issued_once_and_not_due_before_its_issue_date()
    {
        var invoice = Draft(null, new InvoiceLineDraft("Work", 1m, 10m, _services.Id));

        Assert.Equal("invoice_due_before_issue", CodeOf(() => invoice.Issue(1, IssueDate, IssueDate.AddDays(-1))));
        invoice.Issue(1, IssueDate, IssueDate);
        Assert.Equal("invoice_not_draft", CodeOf(() => invoice.Issue(2, IssueDate, IssueDate)));
    }

    [Fact]
    public void A_payment_applies_its_allocations_and_posts_bank_against_receivable()
    {
        var first = Issued(100m, 1);
        var second = Issued(80m, 2);
        var invoices = new[] { first, second }.ToDictionary(invoice => invoice.Id);

        var received = Payment.Receive(
            CustomerId,
            IssueDate.AddDays(5),
            150m,
            "CAD",
            _bank.Id,
            [new AllocationDraft(first.Id, 100m), new AllocationDraft(second.Id, 50m)],
            invoices);

        Assert.Equal(
            [new LineDraft(_bank.Id, EntrySide.Debit, 150m), new LineDraft(_receivable.Id, EntrySide.Credit, 150m)],
            received.Posting);
        Assert.Equal((InvoiceStatus.Paid, 0m), (first.Status, first.Outstanding));
        Assert.Equal((InvoiceStatus.Issued, 30m), (second.Status, second.Outstanding));
        Assert.Equal(2, received.Payment.Allocations.Count);
    }

    [Fact]
    public void A_payment_is_checked_against_every_allocation_rule()
    {
        var invoice = Issued(100m);
        var invoices = new[] { invoice }.ToDictionary(invoice => invoice.Id);
        var draft = Draft(null, new InvoiceLineDraft("Work", 1m, 10m, _services.Id));

        string Receive(Guid customerId, DateOnly date, decimal amount, string currency, IReadOnlyDictionary<Guid, Invoice> known, params AllocationDraft[] allocations) =>
            CodeOf(() => Payment.Receive(customerId, date, amount, currency, _bank.Id, allocations, known));

        Assert.Equal("payment_amount_invalid", Receive(CustomerId, IssueDate, 0m, "CAD", invoices, new AllocationDraft(invoice.Id, 10m)));
        Assert.Equal("payment_amount_invalid", Receive(CustomerId, IssueDate, 10.005m, "CAD", invoices, new AllocationDraft(invoice.Id, 10m)));
        Assert.Equal("payment_needs_an_allocation", Receive(CustomerId, IssueDate, 10m, "CAD", invoices));
        Assert.Equal("allocation_duplicated", Receive(CustomerId, IssueDate, 20m, "CAD", invoices, new AllocationDraft(invoice.Id, 10m), new AllocationDraft(invoice.Id, 10m)));
        Assert.Equal("allocation_amount_invalid", Receive(CustomerId, IssueDate, 10m, "CAD", invoices, new AllocationDraft(invoice.Id, 0m)));
        Assert.Equal("invoice_not_found", Receive(CustomerId, IssueDate, 10m, "CAD", invoices, new AllocationDraft(Guid.NewGuid(), 10m)));
        Assert.Equal("invoice_of_another_customer", Receive(Guid.NewGuid(), IssueDate, 10m, "CAD", invoices, new AllocationDraft(invoice.Id, 10m)));
        Assert.Equal("currency_mismatch", Receive(CustomerId, IssueDate, 10m, "USD", invoices, new AllocationDraft(invoice.Id, 10m)));
        Assert.Equal("payment_before_invoice", Receive(CustomerId, IssueDate.AddDays(-1), 10m, "CAD", invoices, new AllocationDraft(invoice.Id, 10m)));
        Assert.Equal("allocation_exceeds_outstanding", Receive(CustomerId, IssueDate, 100.01m, "CAD", invoices, new AllocationDraft(invoice.Id, 100.01m)));
        Assert.Equal("invoice_not_open", Receive(CustomerId, IssueDate, 10m, "CAD", new[] { draft }.ToDictionary(invoice => invoice.Id), new AllocationDraft(draft.Id, 10m)));
    }

    [Fact]
    public void A_payment_must_be_allocated_in_full()
    {
        var invoice = Issued(100m);

        var code = CodeOf(() => Payment.Receive(
            CustomerId,
            IssueDate,
            50m,
            "CAD",
            _bank.Id,
            [new AllocationDraft(invoice.Id, 40m)],
            new[] { invoice }.ToDictionary(invoice => invoice.Id)));

        Assert.Equal("payment_not_fully_allocated", code);
    }

    [Fact]
    public void Only_an_issued_invoice_with_nothing_paid_can_be_voided()
    {
        var draft = Draft(null, new InvoiceLineDraft("Work", 1m, 10m, _services.Id));
        var paid = Issued(100m);
        Payment.Receive(CustomerId, IssueDate, 10m, "CAD", _bank.Id, [new AllocationDraft(paid.Id, 10m)], new[] { paid }.ToDictionary(invoice => invoice.Id));
        var clean = Issued(100m);

        Assert.Equal("invoice_not_voidable", CodeOf(draft.Void));
        Assert.Equal("invoice_not_voidable", CodeOf(paid.Void));
        clean.Void();
        Assert.Equal((InvoiceStatus.Void, 0m), (clean.Status, clean.Outstanding));
    }
}
