namespace Ledger.Domain;

public static class AccountingPeriod
{
    public const int MinYear = 1900;
    public const int MaxYear = 9998;

    public static DateOnly EndOf(int year, int month)
    {
        if (year is < MinYear or > MaxYear || month is < 1 or > 12)
        {
            throw new LedgerRuleException("period_invalid", $"{year}-{month} is not a calendar month between {MinYear} and {MaxYear}.");
        }

        return new DateOnly(year, month, DateTime.DaysInMonth(year, month));
    }

    public static void EnsureEnded(DateOnly periodEnd, DateOnly today)
    {
        if (periodEnd >= today)
        {
            throw new LedgerRuleException("period_not_ended", $"The period ending {periodEnd:yyyy-MM-dd} has not ended yet.");
        }
    }

    public static void EnsureOpen(DateOnly entryDate, DateOnly? closedThrough)
    {
        if (closedThrough is { } closed && entryDate <= closed)
        {
            throw new LedgerRuleException("period_closed", $"The books are closed through {closed:yyyy-MM-dd}, so nothing can be dated {entryDate:yyyy-MM-dd}.");
        }
    }
}
