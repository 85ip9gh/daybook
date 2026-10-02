namespace Ledger.Domain;

public sealed record AccountTotals(Guid AccountId, decimal Debits, decimal Credits)
{
    public decimal BalanceOn(EntrySide normalSide) =>
        normalSide == EntrySide.Debit ? Debits - Credits : Credits - Debits;
}

public sealed record TrialBalanceRow(Guid AccountId, string Code, string Name, AccountType Type, decimal Debit, decimal Credit);

public sealed record TrialBalance(IReadOnlyList<TrialBalanceRow> Rows, decimal TotalDebit, decimal TotalCredit)
{
    public bool IsBalanced => TotalDebit == TotalCredit;

    public static TrialBalance From(IEnumerable<Account> accounts, IEnumerable<AccountTotals> totals)
    {
        var totalsByAccount = totals.ToDictionary(total => total.AccountId);
        var rows = new List<TrialBalanceRow>();

        foreach (var account in accounts.OrderBy(account => account.Code, StringComparer.Ordinal))
        {
            if (!totalsByAccount.TryGetValue(account.Id, out var accountTotals))
            {
                continue;
            }

            var net = accountTotals.Debits - accountTotals.Credits;
            rows.Add(new TrialBalanceRow(
                account.Id,
                account.Code,
                account.Name,
                account.Type,
                net > 0 ? net : 0,
                net < 0 ? -net : 0));
        }

        return new TrialBalance(rows, rows.Sum(row => row.Debit), rows.Sum(row => row.Credit));
    }
}
