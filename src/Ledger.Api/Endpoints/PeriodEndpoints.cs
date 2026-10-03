using System.Security.Claims;
using Ledger.Api.Auth;
using Ledger.Api.Data;
using Ledger.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Api.Endpoints;

internal static class PeriodEndpoints
{
    public static IEndpointRouteBuilder MapPeriodEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/periods").WithTags("Periods");
        group.MapGet("/", ListAsync).RequireAuthorization(Policies.Read);
        group.MapPost("/{year:int}/{month:int}/close", CloseAsync).RequireAuthorization(Policies.Close);
        group.MapGet("/{year:int}/{month:int}/trial-balance", ClosingTrialBalanceAsync).RequireAuthorization(Policies.Read);
        return routes;
    }

    private static async Task<IResult> ListAsync(LedgerDbContext db, CancellationToken cancellationToken)
    {
        var state = await db.LedgerStates.AsNoTracking().SingleAsync(cancellationToken);
        var closes = await db.PeriodCloses.AsNoTracking().OrderBy(close => close.PeriodEnd).ToListAsync(cancellationToken);

        return Results.Ok(new PeriodsResponse(state.ClosedThrough, [.. closes.Select(close => new PeriodCloseResponse(close.PeriodEnd, close.ClosedAt, close.ClosedBy))]));
    }

    private static async Task<IResult> CloseAsync(
        int year,
        int month,
        LedgerDbContext db,
        TimeProvider clock,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var periodEnd = AccountingPeriod.EndOf(year, month);
        AccountingPeriod.EnsureEnded(periodEnd, DateOnly.FromDateTime(now.UtcDateTime));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (!await db.TryCloseThroughAsync(periodEnd, cancellationToken))
        {
            return Problems.Conflict("period_already_closed", $"The books are already closed through {periodEnd:yyyy-MM-dd} or later.");
        }

        var close = new PeriodClose { PeriodEnd = periodEnd, ClosedAt = now, ClosedBy = user.Name() };
        db.PeriodCloses.Add(close);
        await db.SaveChangesAsync(cancellationToken);
        await db.SnapshotBalancesAsync(periodEnd, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Results.Created($"/periods/{year}/{month}/trial-balance", new PeriodCloseResponse(close.PeriodEnd, close.ClosedAt, close.ClosedBy));
    }

    private static async Task<IResult> ClosingTrialBalanceAsync(int year, int month, LedgerDbContext db, CancellationToken cancellationToken)
    {
        var periodEnd = AccountingPeriod.EndOf(year, month);
        if (!await db.PeriodCloses.AnyAsync(close => close.PeriodEnd == periodEnd, cancellationToken))
        {
            return Problems.NotFound("period_close_not_found", $"No close is recorded for the period ending {periodEnd:yyyy-MM-dd}.");
        }

        var balances = await db.PeriodBalances.AsNoTracking().Where(balance => balance.PeriodEnd == periodEnd).ToListAsync(cancellationToken);
        var accounts = await db.Accounts.AsNoTracking().ToListAsync(cancellationToken);
        var trialBalance = TrialBalance.From(accounts, balances.Select(balance => new AccountTotals(balance.AccountId, balance.Debits, balance.Credits)));

        return Results.Ok(new TrialBalanceResponse(periodEnd, trialBalance.Rows, trialBalance.TotalDebit, trialBalance.TotalCredit));
    }
}
