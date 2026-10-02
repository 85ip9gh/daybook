using Ledger.Api.Data;
using Ledger.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Api.Endpoints;

internal static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/trial-balance", TrialBalanceAsync).WithTags("Reports");
        return routes;
    }

    private static async Task<IResult> TrialBalanceAsync(
        DateOnly? asOf,
        LedgerDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var date = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var totals = await db.TotalsAsync(date, null, cancellationToken);
        var accounts = await db.Accounts.AsNoTracking().ToListAsync(cancellationToken);
        var trialBalance = TrialBalance.From(accounts, totals);

        return Results.Ok(new TrialBalanceResponse(date, trialBalance.Rows, trialBalance.TotalDebit, trialBalance.TotalCredit));
    }
}
