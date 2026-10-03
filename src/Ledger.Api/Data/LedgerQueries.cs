using Ledger.Domain;
using Ledger.Domain.Banking;
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

    public static async Task<DateOnly?> HoldPeriodsOpenAsync(this LedgerDbContext db, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("The period lock is only held for the life of a transaction.");
        }

        var state = await db.LedgerStates
            .FromSqlRaw("SELECT Id, ClosedThrough FROM LedgerState WITH (HOLDLOCK) WHERE Id = 1")
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return state.Single().ClosedThrough;
    }

    public static async Task<bool> TryCloseThroughAsync(this LedgerDbContext db, DateOnly periodEnd, CancellationToken cancellationToken)
    {
        var advanced = await db.Database.ExecuteSqlAsync(
            $"UPDATE LedgerState SET ClosedThrough = {periodEnd} WHERE Id = 1 AND (ClosedThrough IS NULL OR ClosedThrough < {periodEnd})",
            cancellationToken);

        return advanced == 1;
    }

    public static Task<int> SnapshotBalancesAsync(this LedgerDbContext db, DateOnly periodEnd, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO PeriodBalances (PeriodEnd, AccountId, Debits, Credits)
            SELECT {periodEnd},
                   line.AccountId,
                   SUM(CASE WHEN line.Side = 1 THEN line.Amount ELSE 0 END),
                   SUM(CASE WHEN line.Side = 2 THEN line.Amount ELSE 0 END)
            FROM JournalLines AS line
            JOIN JournalEntries AS entry ON entry.Id = line.JournalEntryId
            WHERE entry.EntryDate <= {periodEnd}
            GROUP BY line.AccountId
            """,
            cancellationToken);

    public static async Task<int> NextNumberAsync(this LedgerDbContext db, string sequence, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A number is only gapless when it is taken inside the transaction that uses it.");
        }

        var values = await db.Database
            .SqlQuery<int>($"UPDATE Sequences SET LastValue = LastValue + 1 OUTPUT inserted.LastValue AS Value WHERE Name = {sequence}")
            .ToListAsync(cancellationToken);

        return values.Single();
    }

    public static async Task<List<LedgerItem>> UnreconciledAsync(
        this LedgerDbContext db,
        Guid accountId,
        DateOnly earliest,
        DateOnly latest,
        CancellationToken cancellationToken)
    {
        var lines = await (
            from line in db.JournalLines.AsNoTracking()
            join entry in db.JournalEntries.AsNoTracking() on line.JournalEntryId equals entry.Id
            where line.AccountId == accountId
                && entry.EntryDate >= earliest
                && entry.EntryDate <= latest
                && !db.BankLines.Any(bank => bank.MatchedEntryId == line.JournalEntryId && bank.MatchedLineNumber == line.LineNumber)
            select new { line.JournalEntryId, line.LineNumber, entry.EntryDate, line.Side, line.Amount })
            .ToListAsync(cancellationToken);

        return [.. lines.Select(line => new LedgerItem(line.JournalEntryId, line.LineNumber, line.EntryDate, BankLine.Signed(line.Side, line.Amount)))];
    }

    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: DuplicateIndexRow or DuplicateConstraintRow };
}
