namespace Ledger.Domain.Banking;

public readonly record struct BankItem(Guid BankLineId, DateOnly Date, decimal Amount);

public readonly record struct LedgerItem(Guid JournalEntryId, int LineNumber, DateOnly Date, decimal SignedAmount);

public readonly record struct Match(Guid BankLineId, Guid JournalEntryId, int LineNumber);

public static class Matcher
{
    public const string Actor = "auto-match";

    public static IReadOnlyList<Match> Pair(IEnumerable<BankItem> bank, IEnumerable<LedgerItem> ledger, int windowDays)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(windowDays);

        var ledgerByAmount = ledger
            .GroupBy(item => item.SignedAmount)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(item => item.Date).ThenBy(item => item.JournalEntryId).ThenBy(item => item.LineNumber).ToList());

        var matches = new List<Match>();

        foreach (var group in bank.GroupBy(item => item.Amount).OrderBy(group => group.Key))
        {
            if (!ledgerByAmount.TryGetValue(group.Key, out var candidates))
            {
                continue;
            }

            var lines = group.OrderBy(item => item.Date).ThenBy(item => item.BankLineId).ToList();
            var bankIndex = 0;
            var ledgerIndex = 0;

            while (bankIndex < lines.Count && ledgerIndex < candidates.Count)
            {
                var daysApart = lines[bankIndex].Date.DayNumber - candidates[ledgerIndex].Date.DayNumber;

                if (Math.Abs(daysApart) <= windowDays)
                {
                    matches.Add(new Match(lines[bankIndex].BankLineId, candidates[ledgerIndex].JournalEntryId, candidates[ledgerIndex].LineNumber));
                    bankIndex++;
                    ledgerIndex++;
                }
                else if (daysApart > 0)
                {
                    ledgerIndex++;
                }
                else
                {
                    bankIndex++;
                }
            }
        }

        return matches;
    }
}
