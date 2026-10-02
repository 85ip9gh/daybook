namespace Ledger.Domain.Tests;

public class JournalEntryTests
{
    private static readonly DateOnly EntryDate = new(2026, 10, 1);
    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly Account _cash = Account.Open("1000", "Cash", AccountType.Asset, "CAD");
    private readonly Account _revenue = Account.Open("4000", "Revenue", AccountType.Revenue, "CAD");
    private readonly Account _tax = Account.Open("2100", "Sales tax payable", AccountType.Liability, "CAD");

    private Dictionary<Guid, Account> Accounts => new[] { _cash, _revenue, _tax }.ToDictionary(account => account.Id);

    private JournalEntry Post(params LineDraft[] lines) =>
        JournalEntry.Post(EntryDate, "Invoice 1001", "CAD", lines, Accounts, null, RecordedAt);

    private static string CodeOf(Action act) => Assert.Throws<LedgerRuleException>(act).Code;

    [Fact]
    public void A_balanced_entry_posts_with_numbered_lines()
    {
        var entry = Post(
            new LineDraft(_cash.Id, EntrySide.Debit, 115m),
            new LineDraft(_revenue.Id, EntrySide.Credit, 100m),
            new LineDraft(_tax.Id, EntrySide.Credit, 15m));

        Assert.Equal([1, 2, 3], entry.Lines.Select(line => line.LineNumber));
        Assert.All(entry.Lines, line => Assert.Equal(entry.Id, line.JournalEntryId));
        Assert.Equal("CAD", entry.Currency);
        Assert.Null(entry.ReversesEntryId);
        Assert.Equal(RecordedAt, entry.RecordedAt);
    }

    [Fact]
    public void An_unbalanced_entry_is_rejected()
    {
        var code = CodeOf(() => Post(
            new LineDraft(_cash.Id, EntrySide.Debit, 100m),
            new LineDraft(_revenue.Id, EntrySide.Credit, 99.9999m)));

        Assert.Equal("entry_unbalanced", code);
    }

    [Fact]
    public void An_entry_with_one_line_is_rejected()
    {
        var code = CodeOf(() => Post(new LineDraft(_cash.Id, EntrySide.Debit, 100m)));

        Assert.Equal("entry_needs_two_lines", code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-25)]
    public void A_line_amount_must_be_positive(int amount)
    {
        var code = CodeOf(() => Post(
            new LineDraft(_cash.Id, EntrySide.Debit, amount),
            new LineDraft(_revenue.Id, EntrySide.Credit, amount)));

        Assert.Equal("line_amount_not_positive", code);
    }

    [Fact]
    public void A_line_amount_carries_at_most_four_decimal_places()
    {
        var code = CodeOf(() => Post(
            new LineDraft(_cash.Id, EntrySide.Debit, 10.00001m),
            new LineDraft(_revenue.Id, EntrySide.Credit, 10.00001m)));

        Assert.Equal("line_amount_too_precise", code);
    }

    [Fact]
    public void A_line_amount_cannot_exceed_the_column_range()
    {
        var code = CodeOf(() => Post(
            new LineDraft(_cash.Id, EntrySide.Debit, JournalEntry.MaxAmount + 1),
            new LineDraft(_revenue.Id, EntrySide.Credit, JournalEntry.MaxAmount + 1)));

        Assert.Equal("line_amount_too_large", code);
    }

    [Fact]
    public void A_line_side_outside_the_enum_is_rejected()
    {
        var code = CodeOf(() => Post(
            new LineDraft(_cash.Id, (EntrySide)7, 10m),
            new LineDraft(_revenue.Id, EntrySide.Credit, 10m)));

        Assert.Equal("line_side_invalid", code);
    }

    [Fact]
    public void A_line_against_an_unknown_account_is_rejected()
    {
        var code = CodeOf(() => Post(
            new LineDraft(Guid.NewGuid(), EntrySide.Debit, 10m),
            new LineDraft(_revenue.Id, EntrySide.Credit, 10m)));

        Assert.Equal("account_not_found", code);
    }

    [Fact]
    public void A_line_against_an_inactive_account_is_rejected()
    {
        _cash.Deactivate();

        var code = CodeOf(() => Post(
            new LineDraft(_cash.Id, EntrySide.Debit, 10m),
            new LineDraft(_revenue.Id, EntrySide.Credit, 10m)));

        Assert.Equal("account_inactive", code);
    }

    [Fact]
    public void An_entry_in_a_currency_the_account_does_not_hold_is_rejected()
    {
        var code = CodeOf(() => JournalEntry.Post(
            EntryDate,
            "Invoice 1001",
            "USD",
            [new LineDraft(_cash.Id, EntrySide.Debit, 10m), new LineDraft(_revenue.Id, EntrySide.Credit, 10m)],
            Accounts,
            null,
            RecordedAt));

        Assert.Equal("currency_mismatch", code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_description_is_required(string description)
    {
        var code = CodeOf(() => JournalEntry.Post(
            EntryDate,
            description,
            "CAD",
            [new LineDraft(_cash.Id, EntrySide.Debit, 10m), new LineDraft(_revenue.Id, EntrySide.Credit, 10m)],
            Accounts,
            null,
            RecordedAt));

        Assert.Equal("entry_description_invalid", code);
    }

    [Fact]
    public void An_entry_date_is_required()
    {
        var code = CodeOf(() => JournalEntry.Post(
            default,
            "Invoice 1001",
            "CAD",
            [new LineDraft(_cash.Id, EntrySide.Debit, 10m), new LineDraft(_revenue.Id, EntrySide.Credit, 10m)],
            Accounts,
            null,
            RecordedAt));

        Assert.Equal("entry_date_required", code);
    }

    [Fact]
    public void A_reversal_mirrors_every_line_and_points_at_the_original()
    {
        var original = Post(
            new LineDraft(_cash.Id, EntrySide.Debit, 115m),
            new LineDraft(_revenue.Id, EntrySide.Credit, 100m),
            new LineDraft(_tax.Id, EntrySide.Credit, 15m));

        var reversal = original.Reverse(EntryDate.AddDays(1), null, RecordedAt.AddDays(1));

        Assert.Equal(original.Id, reversal.ReversesEntryId);
        Assert.NotEqual(original.Id, reversal.Id);
        Assert.Equal(
            [(_cash.Id, EntrySide.Credit, 115m), (_revenue.Id, EntrySide.Debit, 100m), (_tax.Id, EntrySide.Debit, 15m)],
            reversal.Lines.Select(line => (line.AccountId, line.Side, line.Amount)));
        Assert.StartsWith("Reversal: ", reversal.Description);
    }

    [Fact]
    public void A_reversal_cannot_be_reversed()
    {
        var reversal = Post(
            new LineDraft(_cash.Id, EntrySide.Debit, 10m),
            new LineDraft(_revenue.Id, EntrySide.Credit, 10m)).Reverse(EntryDate, null, RecordedAt);

        var code = CodeOf(() => reversal.Reverse(EntryDate, null, RecordedAt));

        Assert.Equal("reversal_not_reversible", code);
    }

    [Fact]
    public void A_reversal_cannot_be_dated_before_the_original()
    {
        var original = Post(
            new LineDraft(_cash.Id, EntrySide.Debit, 10m),
            new LineDraft(_revenue.Id, EntrySide.Credit, 10m));

        var code = CodeOf(() => original.Reverse(EntryDate.AddDays(-1), null, RecordedAt));

        Assert.Equal("reversal_before_original", code);
    }

    [Fact]
    public void A_reversal_of_a_long_description_stays_within_the_column()
    {
        var original = JournalEntry.Post(
            EntryDate,
            new string('x', JournalEntry.MaxDescriptionLength),
            "CAD",
            [new LineDraft(_cash.Id, EntrySide.Debit, 10m), new LineDraft(_revenue.Id, EntrySide.Credit, 10m)],
            Accounts,
            null,
            RecordedAt);

        var reversal = original.Reverse(EntryDate, null, RecordedAt);

        Assert.Equal(JournalEntry.MaxDescriptionLength, reversal.Description.Length);
    }
}
