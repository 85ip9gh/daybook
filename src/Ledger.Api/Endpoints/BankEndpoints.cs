using System.Security.Claims;
using System.Text;
using Ledger.Api.Auth;
using Ledger.Api.Data;
using Ledger.Domain;
using Ledger.Domain.Banking;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Api.Endpoints;

public sealed record StatementResponse(
    Guid Id,
    Guid AccountId,
    StatementStatus Status,
    int Attempts,
    int LineCount,
    int NewLineCount,
    int MatchedCount,
    string? Error,
    string ImportedBy,
    DateTimeOffset ImportedAt,
    DateTimeOffset? ProcessedAt);

public sealed record BankLineResponse(
    Guid Id,
    DateOnly Date,
    string Reference,
    string Description,
    decimal Amount,
    Guid? MatchedEntryId,
    int? MatchedLineNumber,
    string? MatchedBy);

public sealed record LedgerLineResponse(Guid JournalEntryId, int LineNumber, DateOnly EntryDate, decimal Amount);

public sealed record ReconciliationResponse(
    Guid AccountId,
    DateOnly AsOf,
    decimal BookBalance,
    decimal BankTotal,
    IReadOnlyList<BankLineResponse> UnmatchedBankLines,
    decimal UnmatchedBankTotal,
    IReadOnlyList<LedgerLineResponse> UnreconciledLedgerLines,
    decimal UnreconciledLedgerTotal);

public sealed record MatchRequest(Guid JournalEntryId, int LineNumber);

internal static class BankEndpoints
{
    public const int RecentStatements = 20;

    public static IEndpointRouteBuilder MapBankEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/").WithTags("Banking");
        group.MapPost("/bank-accounts/{accountId:guid}/statements", ImportAsync).RequireAuthorization(Policies.Record);
        group.MapGet("/bank-accounts/{accountId:guid}/reconciliation", ReconciliationAsync).RequireAuthorization(Policies.Read);
        group.MapGet("/bank-accounts/{accountId:guid}/statements", StatementsAsync).RequireAuthorization(Policies.Read);
        group.MapGet("/bank-statements/{id:guid}", StatementAsync).RequireAuthorization(Policies.Read);
        group.MapPost("/bank-lines/{id:guid}/match", MatchAsync).RequireAuthorization(Policies.Post);
        group.MapPost("/bank-lines/{id:guid}/unmatch", UnmatchAsync).RequireAuthorization(Policies.Post);
        return routes;
    }

    private static async Task<IResult> ImportAsync(
        Guid accountId,
        HttpRequest request,
        LedgerDbContext db,
        ClaimsPrincipal user,
        TimeProvider clock,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(account => account.Id == accountId, cancellationToken);
        if (account is null)
        {
            return Problems.NotFound("account_not_found", $"Account {accountId} does not exist.");
        }

        var statement = BankStatement.Import(account, await ReadBodyAsync(request, cancellationToken), user.Name(), clock.GetUtcNow());

        var existing = await FindAsync(db, accountId, statement.ContentHash, cancellationToken);
        if (existing is not null)
        {
            return Replay(existing, http);
        }

        db.BankStatements.Add(statement);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            db.ChangeTracker.Clear();
            var winner = await FindAsync(db, accountId, statement.ContentHash, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return Replay(winner, http);
        }

        return Results.Accepted($"/bank-statements/{statement.Id}", ToResponse(statement));
    }

    private static async Task<string> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var buffer = new char[BankStatement.MaxContentLength + 1];
        var length = 0;
        int read;

        while (length < buffer.Length && (read = await reader.ReadAsync(buffer.AsMemory(length), cancellationToken)) > 0)
        {
            length += read;
        }

        return new string(buffer, 0, length);
    }

    private static Task<BankStatement?> FindAsync(LedgerDbContext db, Guid accountId, string hash, CancellationToken cancellationToken) =>
        db.BankStatements.AsNoTracking().SingleOrDefaultAsync(statement => statement.AccountId == accountId && statement.ContentHash == hash, cancellationToken);

    private static IResult Replay(BankStatement statement, HttpContext http)
    {
        http.Response.Headers[Idempotency.ReplayHeader] = "true";
        return Results.Ok(ToResponse(statement));
    }

    private static async Task<IResult> StatementsAsync(Guid accountId, LedgerDbContext db, CancellationToken cancellationToken)
    {
        var statements = await db.BankStatements
            .AsNoTracking()
            .Where(statement => statement.AccountId == accountId)
            .OrderByDescending(statement => statement.ImportedAt)
            .Take(RecentStatements)
            .Select(statement => new StatementResponse(
                statement.Id,
                statement.AccountId,
                statement.Status,
                statement.Attempts,
                statement.LineCount,
                statement.NewLineCount,
                statement.MatchedCount,
                statement.Error,
                statement.ImportedBy,
                statement.ImportedAt,
                statement.ProcessedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(statements);
    }

    private static async Task<IResult> StatementAsync(Guid id, LedgerDbContext db, CancellationToken cancellationToken)
    {
        var statement = await db.BankStatements.AsNoTracking().SingleOrDefaultAsync(statement => statement.Id == id, cancellationToken);
        return statement is null
            ? Problems.NotFound("statement_not_found", $"Bank statement {id} does not exist.")
            : Results.Ok(ToResponse(statement));
    }

    private static async Task<IResult> ReconciliationAsync(
        Guid accountId,
        DateOnly? asOf,
        LedgerDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!await db.Accounts.AnyAsync(account => account.Id == accountId, cancellationToken))
        {
            return Problems.NotFound("account_not_found", $"Account {accountId} does not exist.");
        }

        var date = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var totals = (await db.TotalsAsync(date, accountId, cancellationToken)).SingleOrDefault() ?? new AccountTotals(accountId, 0, 0);

        var bankLines = db.BankLines.AsNoTracking().Where(line => line.AccountId == accountId && line.Date <= date);
        var bankTotal = await bankLines.SumAsync(line => line.Amount, cancellationToken);
        var unmatched = await bankLines
            .Where(line => line.MatchedEntryId == null)
            .OrderBy(line => line.Date)
            .ThenBy(line => line.Reference)
            .ToListAsync(cancellationToken);
        var unreconciled = await db.UnreconciledAsync(accountId, DateOnly.MinValue, date, cancellationToken);

        return Results.Ok(new ReconciliationResponse(
            accountId,
            date,
            totals.Debits - totals.Credits,
            bankTotal,
            [.. unmatched.Select(ToResponse)],
            unmatched.Sum(line => line.Amount),
            [.. unreconciled
                .OrderBy(item => item.Date)
                .Select(item => new LedgerLineResponse(item.JournalEntryId, item.LineNumber, item.Date, item.SignedAmount))],
            unreconciled.Sum(item => item.SignedAmount)));
    }

    private static async Task<IResult> MatchAsync(
        Guid id,
        MatchRequest request,
        LedgerDbContext db,
        ClaimsPrincipal user,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var line = await db.BankLines.SingleOrDefaultAsync(line => line.Id == id, cancellationToken);
        if (line is null)
        {
            return NotFound(id);
        }

        var ledgerLine = await db.JournalLines
            .AsNoTracking()
            .SingleOrDefaultAsync(
                ledgerLine => ledgerLine.JournalEntryId == request.JournalEntryId && ledgerLine.LineNumber == request.LineNumber,
                cancellationToken);

        if (ledgerLine is null || ledgerLine.AccountId != line.AccountId)
        {
            throw new LedgerRuleException("ledger_line_not_on_account", "That journal line does not exist on this bank account.");
        }

        line.MatchTo(ledgerLine.JournalEntryId, ledgerLine.LineNumber, BankLine.Signed(ledgerLine.Side, ledgerLine.Amount), user.Name(), clock.GetUtcNow());

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Changed(id);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return Problems.Conflict("ledger_line_already_matched", "That journal line is already matched to another bank line.");
        }

        return Results.Ok(ToResponse(line));
    }

    private static async Task<IResult> UnmatchAsync(Guid id, LedgerDbContext db, CancellationToken cancellationToken)
    {
        var line = await db.BankLines.SingleOrDefaultAsync(line => line.Id == id, cancellationToken);
        if (line is null)
        {
            return NotFound(id);
        }

        line.Unmatch();

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Changed(id);
        }

        return Results.Ok(ToResponse(line));
    }

    private static StatementResponse ToResponse(BankStatement statement) =>
        new(
            statement.Id,
            statement.AccountId,
            statement.Status,
            statement.Attempts,
            statement.LineCount,
            statement.NewLineCount,
            statement.MatchedCount,
            statement.Error,
            statement.ImportedBy,
            statement.ImportedAt,
            statement.ProcessedAt);

    private static BankLineResponse ToResponse(BankLine line) =>
        new(line.Id, line.Date, line.Reference, line.Description, line.Amount, line.MatchedEntryId, line.MatchedLineNumber, line.MatchedBy);

    private static IResult NotFound(Guid id) => Problems.NotFound("bank_line_not_found", $"Bank line {id} does not exist.");

    private static IResult Changed(Guid id) =>
        Problems.Conflict("bank_line_changed", $"Bank line {id} was changed by another request. Read it again and retry.");
}
