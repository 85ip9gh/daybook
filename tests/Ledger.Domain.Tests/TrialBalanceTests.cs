namespace Ledger.Domain.Tests;

public class TrialBalanceTests
{
    private static readonly DateOnly EntryDate = new(2026, 10, 1);

    [Fact]
    public void A_net_balance_lands_in_the_column_of_its_sign()
    {
        var cash = Account.Open("1000", "Cash", AccountType.Asset, "CAD");
        var payable = Account.Open("2000", "Accounts payable", AccountType.Liability, "CAD");

        var trialBalance = TrialBalance.From(
            [payable, cash],
            [new AccountTotals(cash.Id, 300m, 50m), new AccountTotals(payable.Id, 50m, 300m)]);

        Assert.Equal(["1000", "2000"], trialBalance.Rows.Select(row => row.Code));
        Assert.Equal((250m, 0m), (trialBalance.Rows[0].Debit, trialBalance.Rows[0].Credit));
        Assert.Equal((0m, 250m), (trialBalance.Rows[1].Debit, trialBalance.Rows[1].Credit));
        Assert.True(trialBalance.IsBalanced);
    }

    [Fact]
    public void An_overdrawn_asset_shows_as_a_credit_balance()
    {
        var cash = Account.Open("1000", "Cash", AccountType.Asset, "CAD");

        var trialBalance = TrialBalance.From([cash], [new AccountTotals(cash.Id, 10m, 40m)]);

        Assert.Equal((0m, 30m), (trialBalance.Rows[0].Debit, trialBalance.Rows[0].Credit));
        Assert.False(trialBalance.IsBalanced);
    }

    [Fact]
    public void Accounts_without_activity_are_left_out()
    {
        var cash = Account.Open("1000", "Cash", AccountType.Asset, "CAD");
        var idle = Account.Open("1900", "Idle", AccountType.Asset, "CAD");

        var trialBalance = TrialBalance.From([cash, idle], [new AccountTotals(cash.Id, 10m, 10m)]);

        Assert.Equal(["1000"], trialBalance.Rows.Select(row => row.Code));
    }

    [Theory]
    [InlineData(EntrySide.Debit, 70)]
    [InlineData(EntrySide.Credit, -70)]
    public void A_balance_is_signed_by_the_normal_side(EntrySide normalSide, int expected)
    {
        var totals = new AccountTotals(Guid.NewGuid(), 100m, 30m);

        Assert.Equal(expected, totals.BalanceOn(normalSide));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20261002)]
    [InlineData(987654321)]
    public void Any_set_of_posted_entries_and_reversals_yields_a_balanced_trial_balance(int seed)
    {
        var random = new Random(seed);
        var accounts = Enumerable.Range(0, 8)
            .Select(index => Account.Open($"{1000 + index}", $"Account {index}", (AccountType)(index % 5 + 1), "CAD"))
            .ToList();
        var byId = accounts.ToDictionary(account => account.Id);
        var entries = new List<JournalEntry>();

        for (var index = 0; index < 500; index++)
        {
            var debitCount = random.Next(1, 4);
            var creditCount = random.Next(1, 4);
            var debits = Enumerable.Range(0, debitCount).Select(_ => random.Next(1, 5_000_000) / 10_000m).ToList();
            var credits = Split(debits.Sum(), creditCount, random);

            var lines = debits
                .Select(amount => new LineDraft(accounts[random.Next(accounts.Count)].Id, EntrySide.Debit, amount))
                .Concat(credits.Select(amount => new LineDraft(accounts[random.Next(accounts.Count)].Id, EntrySide.Credit, amount)))
                .ToList();

            var entry = JournalEntry.Post(EntryDate, $"Entry {index}", "CAD", lines, byId, null, "poster", DateTimeOffset.UnixEpoch);
            entries.Add(entry);

            if (random.Next(5) == 0)
            {
                entries.Add(entry.Reverse(EntryDate, null, "poster", DateTimeOffset.UnixEpoch));
            }
        }

        var totals = entries
            .SelectMany(entry => entry.Lines)
            .GroupBy(line => line.AccountId)
            .Select(group => new AccountTotals(
                group.Key,
                group.Where(line => line.Side == EntrySide.Debit).Sum(line => line.Amount),
                group.Where(line => line.Side == EntrySide.Credit).Sum(line => line.Amount)));

        var trialBalance = TrialBalance.From(accounts, totals);

        Assert.True(trialBalance.IsBalanced);
        Assert.True(trialBalance.TotalDebit > 0);
    }

    private static List<decimal> Split(decimal total, int parts, Random random)
    {
        var units = (long)(total * 10_000m);
        var amounts = new List<decimal>();

        for (var part = parts; part > 1 && units > 1; part--)
        {
            var take = random.NextInt64(1, units);
            amounts.Add(take / 10_000m);
            units -= take;
        }

        amounts.Add(units / 10_000m);
        return amounts;
    }
}
