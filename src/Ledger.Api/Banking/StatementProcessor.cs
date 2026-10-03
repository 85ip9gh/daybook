using Ledger.Api.Data;
using Ledger.Domain.Banking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ledger.Api.Banking;

public sealed class ReconciliationOptions
{
    public const string Section = "Reconciliation";

    public bool WorkerEnabled { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(5);

    public int MatchWindowDays { get; set; } = 3;
}

public sealed class StatementProcessor(LedgerDbContext db, TimeProvider clock, IOptions<ReconciliationOptions> options)
{
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var staleBefore = now - options.Value.Lease;

        var claimed = await db.Database
            .SqlQuery<Guid>(
                $"""
                UPDATE TOP (1) statement
                SET Status = {(byte)StatementStatus.Processing}, Attempts = Attempts + 1, ClaimedAt = {now}
                OUTPUT inserted.Id AS Value
                FROM BankStatements AS statement WITH (ROWLOCK, READPAST, UPDLOCK)
                WHERE statement.Status = {(byte)StatementStatus.Pending}
                   OR (statement.Status = {(byte)StatementStatus.Processing} AND statement.ClaimedAt < {staleBefore})
                """)
            .ToListAsync(cancellationToken);

        if (claimed.Count == 0)
        {
            return false;
        }

        await ProcessAsync(claimed[0], now, cancellationToken);
        return true;
    }

    private async Task ProcessAsync(Guid statementId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var statement = await db.BankStatements.SingleAsync(statement => statement.Id == statementId, cancellationToken);
        var parsed = StatementParser.Parse(statement.Content);

        if (!parsed.IsValid)
        {
            statement.Fail(string.Join(" ", parsed.Errors), now);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var references = parsed.Lines.Select(line => line.Reference).ToList();
            var known = await db.BankLines
                .Where(line => line.AccountId == statement.AccountId && references.Contains(line.Reference))
                .Select(line => line.Reference)
                .ToListAsync(cancellationToken);
            var knownReferences = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);

            var fresh = parsed.Lines
                .Where(line => !knownReferences.Contains(line.Reference))
                .Select(line => BankLine.From(statement, line))
                .ToList();
            db.BankLines.AddRange(fresh);
            await db.SaveChangesAsync(cancellationToken);

            var matched = await MatchAsync(statement.AccountId, now, cancellationToken);

            statement.Complete(parsed.Lines.Count, fresh.Count, matched, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            db.ChangeTracker.Clear();
            var lost = await db.BankStatements.SingleAsync(statement => statement.Id == statementId, cancellationToken);
            lost.Release(now);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<int> MatchAsync(Guid accountId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var unmatched = await db.BankLines
            .Where(line => line.AccountId == accountId && line.MatchedEntryId == null)
            .ToListAsync(cancellationToken);

        if (unmatched.Count == 0)
        {
            return 0;
        }

        var window = options.Value.MatchWindowDays;
        var ledger = await db.UnreconciledAsync(
            accountId,
            unmatched.Min(line => line.Date).AddDays(-window),
            unmatched.Max(line => line.Date).AddDays(window),
            cancellationToken);

        var bankById = unmatched.ToDictionary(line => line.Id);
        var ledgerByKey = ledger.ToDictionary(item => (item.JournalEntryId, item.LineNumber));
        var matches = Matcher.Pair(unmatched.Select(line => new BankItem(line.Id, line.Date, line.Amount)), ledger, window);

        foreach (var match in matches)
        {
            bankById[match.BankLineId].MatchTo(
                match.JournalEntryId,
                match.LineNumber,
                ledgerByKey[(match.JournalEntryId, match.LineNumber)].SignedAmount,
                Matcher.Actor,
                now);
        }

        return matches.Count;
    }
}

internal sealed partial class StatementWorker(
    IServiceScopeFactory scopes,
    IOptions<ReconciliationOptions> options,
    TimeProvider clock,
    ILogger<StatementWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await ProcessOneAsync(stoppingToken))
                {
                }

                await Task.Delay(options.Value.PollInterval, clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogFailure(logger, exception);
                await Task.Delay(options.Value.PollInterval, clock, CancellationToken.None);
            }
        }
    }

    private async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<StatementProcessor>().ProcessNextAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Statement processing failed and will be retried after the poll interval.")]
    private static partial void LogFailure(ILogger logger, Exception exception);
}
