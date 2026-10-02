using Ledger.Api.Data;
using Ledger.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Api.Endpoints;

internal static class JournalEndpoints
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string ReplayHeader = "Idempotent-Replay";

    public static IEndpointRouteBuilder MapJournalEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/journal-entries").WithTags("Journal");
        group.MapPost("/", PostAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/{id:guid}/reversal", ReverseAsync);
        return routes;
    }

    private static async Task<IResult> PostAsync(
        PostEntryRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        LedgerDbContext db,
        TimeProvider clock,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var key = idempotencyKey?.Trim() ?? string.Empty;
        if (key.Length is 0 or > IdempotencyRecord.MaxKeyLength)
        {
            return Problems.BadRequest(
                "idempotency_key_required",
                $"The {IdempotencyKeyHeader} header is required and holds at most {IdempotencyRecord.MaxKeyLength} characters.");
        }

        var hash = RequestHash.Of(request);

        var replay = await ReplayAsync(db, key, hash, http, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var closedThrough = await db.HoldPeriodsOpenAsync(cancellationToken);

        var accountIds = request.Lines.Select(line => line.AccountId).Distinct().ToList();
        var accounts = await db.Accounts
            .Where(account => accountIds.Contains(account.Id))
            .ToDictionaryAsync(account => account.Id, cancellationToken);

        var entry = JournalEntry.Post(
            request.EntryDate,
            request.Description,
            request.Currency,
            [.. request.Lines.Select(line => new LineDraft(line.AccountId, line.Side, line.Amount))],
            accounts,
            closedThrough,
            clock.GetUtcNow());

        db.JournalEntries.Add(entry);
        db.IdempotencyRecords.Add(new IdempotencyRecord { Key = key, RequestHash = hash, JournalEntryId = entry.Id });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var winner = await ReplayAsync(db, key, hash, http, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return winner;
        }

        return Results.Created($"/journal-entries/{entry.Id}", entry.ToResponse());
    }

    private static async Task<IResult?> ReplayAsync(
        LedgerDbContext db,
        string key,
        string hash,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var record = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(record => record.Key == key, cancellationToken);
        if (record is null)
        {
            return null;
        }

        if (record.RequestHash != hash)
        {
            return Problems.Conflict("idempotency_key_reused", $"{IdempotencyKeyHeader} {key} was already used with a different request.");
        }

        var entry = await db.JournalEntries
            .AsNoTracking()
            .Include(entry => entry.Lines)
            .SingleAsync(entry => entry.Id == record.JournalEntryId, cancellationToken);

        http.Response.Headers[ReplayHeader] = "true";
        return Results.Ok(entry.ToResponse());
    }

    private static async Task<IResult> GetAsync(Guid id, LedgerDbContext db, CancellationToken cancellationToken)
    {
        var entry = await db.JournalEntries
            .AsNoTracking()
            .Include(entry => entry.Lines)
            .SingleOrDefaultAsync(entry => entry.Id == id, cancellationToken);

        return entry is null
            ? Problems.NotFound("entry_not_found", $"Journal entry {id} does not exist.")
            : Results.Ok(entry.ToResponse());
    }

    private static async Task<IResult> ReverseAsync(
        Guid id,
        ReverseEntryRequest request,
        LedgerDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var original = await db.JournalEntries
            .AsNoTracking()
            .Include(entry => entry.Lines)
            .SingleOrDefaultAsync(entry => entry.Id == id, cancellationToken);

        if (original is null)
        {
            return Problems.NotFound("entry_not_found", $"Journal entry {id} does not exist.");
        }

        if (await db.JournalEntries.AnyAsync(entry => entry.ReversesEntryId == id, cancellationToken))
        {
            return AlreadyReversed(id);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var closedThrough = await db.HoldPeriodsOpenAsync(cancellationToken);

        var reversal = original.Reverse(request.EntryDate, closedThrough, clock.GetUtcNow());
        db.JournalEntries.Add(reversal);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return AlreadyReversed(id);
        }

        return Results.Created($"/journal-entries/{reversal.Id}", reversal.ToResponse());
    }

    private static IResult AlreadyReversed(Guid id) =>
        Problems.Conflict("entry_already_reversed", $"Journal entry {id} has already been reversed.");
}
