using Ledger.Domain;
using Ledger.Domain.Banking;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Api.Data;

public sealed class StatementRow
{
    public Guid JournalEntryId { get; init; }

    public int LineNumber { get; init; }

    public DateOnly EntryDate { get; init; }

    public string Description { get; init; } = string.Empty;

    public byte Side { get; init; }

    public decimal Amount { get; init; }

    public decimal RunningDebits { get; init; }

    public decimal RunningCredits { get; init; }
}

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
        var closed = await db.PeriodCloses
            .Where(close => close.PeriodEnd <= asOf)
            .MaxAsync(close => (DateOnly?)close.PeriodEnd, cancellationToken);

        var totals = new Dictionary<Guid, AccountTotals>();

        if (closed is not null)
        {
            var opening = await db.PeriodBalances
                .AsNoTracking()
                .Where(balance => balance.PeriodEnd == closed && (accountId == null || balance.AccountId == accountId))
                .ToListAsync(cancellationToken);

            foreach (var balance in opening)
            {
                totals[balance.AccountId] = new AccountTotals(balance.AccountId, balance.Debits, balance.Credits);
            }
        }

        var since = await db.ActivityAsync(closed?.AddDays(1) ?? DateOnly.MinValue, asOf, accountId, cancellationToken);

        foreach (var activity in since)
        {
            totals[activity.AccountId] = totals.TryGetValue(activity.AccountId, out var opening)
                ? new AccountTotals(activity.AccountId, opening.Debits + activity.Debits, opening.Credits + activity.Credits)
                : activity;
        }

        return [.. totals.Values];
    }

    public static async Task<List<AccountTotals>> ActivityAsync(
        this LedgerDbContext db,
        DateOnly earliest,
        DateOnly latest,
        Guid? accountId,
        CancellationToken cancellationToken)
    {
        var lines = db.JournalLines.AsNoTracking().Where(line => line.EntryDate >= earliest && line.EntryDate <= latest);

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

    public static Task<List<StatementRow>> StatementAsync(
        this LedgerDbContext db,
        Guid accountId,
        DateOnly earliest,
        DateOnly latest,
        int take,
        CancellationToken cancellationToken) =>
        db.Database
            .SqlQuery<StatementRow>(
                $"""
                SELECT TOP ({take})
                       line.JournalEntryId,
                       line.LineNumber,
                       line.EntryDate,
                       entry.Description,
                       line.Side,
                       line.Amount,
                       SUM(CASE WHEN line.Side = 1 THEN line.Amount ELSE 0 END)
                           OVER (ORDER BY line.EntryDate, entry.RecordedAt, line.JournalEntryId, line.LineNumber ROWS UNBOUNDED PRECEDING) AS RunningDebits,
                       SUM(CASE WHEN line.Side = 2 THEN line.Amount ELSE 0 END)
                           OVER (ORDER BY line.EntryDate, entry.RecordedAt, line.JournalEntryId, line.LineNumber ROWS UNBOUNDED PRECEDING) AS RunningCredits
                FROM JournalLines AS line
                JOIN JournalEntries AS entry ON entry.Id = line.JournalEntryId
                WHERE line.AccountId = {accountId} AND line.EntryDate >= {earliest} AND line.EntryDate <= {latest}
                ORDER BY line.EntryDate, entry.RecordedAt, line.JournalEntryId, line.LineNumber
                """)
            .ToListAsync(cancellationToken);

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
            WHERE line.EntryDate <= {periodEnd}
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
        var lines = await db.JournalLines
            .AsNoTracking()
            .Where(line => line.AccountId == accountId
                && line.EntryDate >= earliest
                && line.EntryDate <= latest
                && !db.BankLines.Any(bank => bank.MatchedEntryId == line.JournalEntryId && bank.MatchedLineNumber == line.LineNumber))
            .Select(line => new { line.JournalEntryId, line.LineNumber, line.EntryDate, line.Side, line.Amount })
            .ToListAsync(cancellationToken);

        return [.. lines.Select(line => new LedgerItem(line.JournalEntryId, line.LineNumber, line.EntryDate, BankLine.Signed(line.Side, line.Amount)))];
    }

    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: DuplicateIndexRow or DuplicateConstraintRow };
}
