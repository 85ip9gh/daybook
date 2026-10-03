namespace Ledger.Domain.Tests;

public class AccountingPeriodTests
{
    private static readonly DateOnly ClosedThrough = new(2026, 9, 30);
    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly Account _cash = Account.Open("1000", "Cash", AccountType.Asset, "CAD");
    private readonly Account _revenue = Account.Open("4000", "Revenue", AccountType.Revenue, "CAD");

    private JournalEntry Post(DateOnly entryDate, DateOnly? closedThrough) =>
        JournalEntry.Post(
            entryDate,
            "Cash sale",
            "CAD",
            [new LineDraft(_cash.Id, EntrySide.Debit, 10m), new LineDraft(_revenue.Id, EntrySide.Credit, 10m)],
            new[] { _cash, _revenue }.ToDictionary(account => account.Id),
            closedThrough,
            "poster",
            RecordedAt);

    [Theory]
    [InlineData(2026, 2, 28)]
    [InlineData(2024, 2, 29)]
    [InlineData(2026, 12, 31)]
    public void A_period_ends_on_the_last_day_of_its_month(int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), AccountingPeriod.EndOf(year, month));
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(1899, 12)]
    [InlineData(9999, 1)]
    public void A_period_outside_the_calendar_is_rejected(int year, int month)
    {
        var exception = Assert.Throws<LedgerRuleException>(() => AccountingPeriod.EndOf(year, month));

        Assert.Equal("period_invalid", exception.Code);
    }

    [Theory]
    [InlineData(2026, 9, 30)]
    [InlineData(2026, 9, 15)]
    public void A_period_cannot_be_closed_until_its_last_day_has_passed(int year, int month, int day)
    {
        var exception = Assert.Throws<LedgerRuleException>(() =>
            AccountingPeriod.EnsureEnded(new DateOnly(2026, 9, 30), new DateOnly(year, month, day)));

        Assert.Equal("period_not_ended", exception.Code);
    }

    [Fact]
    public void A_period_can_be_closed_the_day_after_it_ends()
    {
        AccountingPeriod.EnsureEnded(new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 1));
    }

    [Theory]
    [InlineData(2026, 9, 30)]
    [InlineData(2026, 1, 1)]
    public void A_post_dated_on_or_before_the_close_is_rejected(int year, int month, int day)
    {
        var exception = Assert.Throws<LedgerRuleException>(() => Post(new DateOnly(year, month, day), ClosedThrough));

        Assert.Equal("period_closed", exception.Code);
    }

    [Fact]
    public void A_post_dated_the_day_after_the_close_is_accepted()
    {
        var entry = Post(ClosedThrough.AddDays(1), ClosedThrough);

        Assert.Equal(ClosedThrough.AddDays(1), entry.EntryDate);
    }

    [Fact]
    public void A_reversal_dated_inside_a_closed_period_is_rejected()
    {
        var original = Post(new DateOnly(2026, 9, 10), null);

        var exception = Assert.Throws<LedgerRuleException>(() => original.Reverse(new DateOnly(2026, 9, 20), ClosedThrough, "poster", RecordedAt));

        Assert.Equal("period_closed", exception.Code);
    }

    [Fact]
    public void An_entry_in_a_closed_period_can_be_reversed_in_an_open_one()
    {
        var original = Post(new DateOnly(2026, 9, 10), null);

        var reversal = original.Reverse(new DateOnly(2026, 10, 1), ClosedThrough, "poster", RecordedAt);

        Assert.Equal(original.Id, reversal.ReversesEntryId);
    }
}
