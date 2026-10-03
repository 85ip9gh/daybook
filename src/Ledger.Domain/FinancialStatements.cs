namespace Ledger.Domain;

public sealed record StatementLine(Guid AccountId, string Code, string Name, decimal Amount);

public sealed record IncomeStatement(
    IReadOnlyList<StatementLine> Revenue,
    IReadOnlyList<StatementLine> Expenses,
    decimal TotalRevenue,
    decimal TotalExpenses)
{
    public decimal NetIncome => TotalRevenue - TotalExpenses;

    public static IncomeStatement From(IEnumerable<Account> accounts, IEnumerable<AccountTotals> activity)
    {
        var lines = FinancialStatements.Lines(accounts, activity);
        var revenue = lines.GetValueOrDefault(AccountType.Revenue, []);
        var expenses = lines.GetValueOrDefault(AccountType.Expense, []);

        return new IncomeStatement(revenue, expenses, revenue.Sum(line => line.Amount), expenses.Sum(line => line.Amount));
    }
}

public sealed record BalanceSheet(
    IReadOnlyList<StatementLine> Assets,
    IReadOnlyList<StatementLine> Liabilities,
    IReadOnlyList<StatementLine> Equity,
    decimal TotalAssets,
    decimal TotalLiabilities,
    decimal TotalEquity,
    decimal CurrentEarnings)
{
    public bool IsBalanced => TotalAssets == TotalLiabilities + TotalEquity + CurrentEarnings;

    public static BalanceSheet From(IEnumerable<Account> accounts, IEnumerable<AccountTotals> totals)
    {
        var lines = FinancialStatements.Lines(accounts, totals);
        var assets = lines.GetValueOrDefault(AccountType.Asset, []);
        var liabilities = lines.GetValueOrDefault(AccountType.Liability, []);
        var equity = lines.GetValueOrDefault(AccountType.Equity, []);
        var earnings = lines.GetValueOrDefault(AccountType.Revenue, []).Sum(line => line.Amount)
            - lines.GetValueOrDefault(AccountType.Expense, []).Sum(line => line.Amount);

        return new BalanceSheet(
            assets,
            liabilities,
            equity,
            assets.Sum(line => line.Amount),
            liabilities.Sum(line => line.Amount),
            equity.Sum(line => line.Amount),
            earnings);
    }
}

internal static class FinancialStatements
{
    public static Dictionary<AccountType, List<StatementLine>> Lines(IEnumerable<Account> accounts, IEnumerable<AccountTotals> totals)
    {
        var totalsByAccount = totals.ToDictionary(total => total.AccountId);

        return accounts
            .Where(account => totalsByAccount.ContainsKey(account.Id))
            .OrderBy(account => account.Code, StringComparer.Ordinal)
            .GroupBy(account => account.Type)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(account => new StatementLine(
                        account.Id,
                        account.Code,
                        account.Name,
                        totalsByAccount[account.Id].BalanceOn(account.Type.NormalSide())))
                    .ToList());
    }
}
