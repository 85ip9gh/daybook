using Ledger.Domain.Receivables;

namespace Ledger.Domain.Tests;

public class ReportTests
{
    private static readonly DateOnly AsOf = new(2026, 10, 31);

    private readonly Account _cash = Account.Open("1000", "Cash", AccountType.Asset, "CAD");
    private readonly Account _loan = Account.Open("2000", "Loan", AccountType.Liability, "CAD");
    private readonly Account _capital = Account.Open("3000", "Capital", AccountType.Equity, "CAD");
    private readonly Account _sales = Account.Open("4000", "Sales", AccountType.Revenue, "CAD");
    private readonly Account _rent = Account.Open("5000", "Rent", AccountType.Expense, "CAD");
    private readonly Account _idle = Account.Open("5900", "Idle", AccountType.Expense, "CAD");

    private Account[] Accounts => [_rent, _sales, _capital, _loan, _cash, _idle];

    private AccountTotals[] Totals =>
    [
        new(_cash.Id, 1700m, 400m),
        new(_loan.Id, 0m, 500m),
        new(_capital.Id, 0m, 600m),
        new(_sales.Id, 50m, 650m),
        new(_rent.Id, 400m, 0m),
    ];

    [Fact]
    public void An_income_statement_nets_revenue_against_expenses()
    {
        var statement = IncomeStatement.From(Accounts, Totals);

        Assert.Equal([new StatementLine(_sales.Id, "4000", "Sales", 600m)], statement.Revenue);
        Assert.Equal([new StatementLine(_rent.Id, "5000", "Rent", 400m)], statement.Expenses);
        Assert.Equal((600m, 400m, 200m), (statement.TotalRevenue, statement.TotalExpenses, statement.NetIncome));
    }

    [Fact]
    public void A_balance_sheet_balances_once_current_earnings_are_counted()
    {
        var sheet = BalanceSheet.From(Accounts, Totals);

        Assert.Equal((1300m, 500m, 600m, 200m), (sheet.TotalAssets, sheet.TotalLiabilities, sheet.TotalEquity, sheet.CurrentEarnings));
        Assert.True(sheet.IsBalanced);
    }

    [Fact]
    public void A_balance_sheet_built_from_one_sided_totals_does_not_balance()
    {
        var sheet = BalanceSheet.From(Accounts, [new AccountTotals(_cash.Id, 100m, 0m)]);

        Assert.False(sheet.IsBalanced);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(77)]
    [InlineData(20261003)]
    public void Any_set_of_balanced_entries_yields_a_balanced_balance_sheet(int seed)
    {
        var random = new Random(seed);
        var accounts = Accounts;
        var byId = accounts.ToDictionary(account => account.Id);
        var lines = new List<JournalLine>();

        for (var index = 0; index < 300; index++)
        {
            var amount = random.Next(1, 1_000_000) / 100m;
            var debit = accounts[random.Next(accounts.Length)];
            var credit = accounts[random.Next(accounts.Length)];
            lines.AddRange(JournalEntry.Post(
                AsOf,
                $"Entry {index}",
                "CAD",
                [new LineDraft(debit.Id, EntrySide.Debit, amount), new LineDraft(credit.Id, EntrySide.Credit, amount)],
                byId,
                null,
                "poster",
                DateTimeOffset.UnixEpoch).Lines);
        }

        var totals = lines
            .GroupBy(line => line.AccountId)
            .Select(group => new AccountTotals(
                group.Key,
                group.Where(line => line.Side == EntrySide.Debit).Sum(line => line.Amount),
                group.Where(line => line.Side == EntrySide.Credit).Sum(line => line.Amount)));

        Assert.True(BalanceSheet.From(accounts, totals).IsBalanced);
        Assert.All(lines, line => Assert.Equal(AsOf, line.EntryDate));
    }

    [Theory]
    [InlineData(1, AgingBucket.Current)]
    [InlineData(0, AgingBucket.Current)]
    [InlineData(-1, AgingBucket.Days1To30)]
    [InlineData(-30, AgingBucket.Days1To30)]
    [InlineData(-31, AgingBucket.Days31To60)]
    [InlineData(-60, AgingBucket.Days31To60)]
    [InlineData(-61, AgingBucket.Days61To90)]
    [InlineData(-90, AgingBucket.Days61To90)]
    [InlineData(-91, AgingBucket.Over90)]
    public void An_invoice_lands_in_the_bucket_for_how_overdue_it_is(int dueInDays, AgingBucket expected)
    {
        Assert.Equal(expected, Aging.BucketFor(AsOf.AddDays(dueInDays), AsOf));
    }

    [Fact]
    public void Ageing_totals_each_customer_by_bucket_and_drops_settled_invoices()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var aged = Aging.Age(
            [
                new OpenInvoice(first, AsOf.AddDays(10), 100m),
                new OpenInvoice(first, AsOf.AddDays(-5), 40m),
                new OpenInvoice(first, AsOf.AddDays(-20), 10m),
                new OpenInvoice(first, AsOf.AddDays(-200), 7m),
                new OpenInvoice(second, AsOf.AddDays(-45), 0m),
            ],
            AsOf);

        var only = Assert.Single(aged);
        Assert.Equal(new AgedCustomer(first, 100m, 50m, 0m, 0m, 7m), only);
        Assert.Equal(157m, only.Total);
    }
}
