namespace Ledger.Domain;

public enum AccountType
{
    Asset = 1,
    Liability = 2,
    Equity = 3,
    Revenue = 4,
    Expense = 5,
}

public enum EntrySide
{
    Debit = 1,
    Credit = 2,
}

public static class AccountTypeExtensions
{
    public static EntrySide NormalSide(this AccountType type) =>
        type is AccountType.Asset or AccountType.Expense ? EntrySide.Debit : EntrySide.Credit;
}
