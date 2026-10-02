using Ledger.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Api.Data;

internal static class LedgerQueries
{
    private const int DuplicateIndexRow = 2601;
    private const int DuplicateConstraintRow = 2627;

    public static async Task<List<AccountTotals>> TotalsAsync(
        this LedgerDbContext db,
        DateOnly asOf,
        Guid? accountId,
        CancellationToken cancellationToken)
    {
        var lines =
            from line in db.JournalLines.AsNoTracking()
            join entry in db.JournalEntries.AsNoTracking() on line.JournalEntryId equals entry.Id
            where entry.EntryDate <= asOf
            select line;

        if (accountId is not null)
        {
            lines = lines.Where(line => line.AccountId == accountId);
        }

        var totals = await lines
            .GroupBy(line => line.AccountId)
            .Select(group => new
            {
                AccountId = group.Key,
                Debits = group.Sum(line => line.Side == EntrySide.Debit ? line.Amount : 0m),
                Credits = group.Sum(line => line.Side == EntrySide.Credit ? line.Amount : 0m),
            })
            .ToListAsync(cancellationToken);

        return [.. totals.Select(total => new AccountTotals(total.AccountId, total.Debits, total.Credits))];
    }

    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: DuplicateIndexRow or DuplicateConstraintRow };
}
