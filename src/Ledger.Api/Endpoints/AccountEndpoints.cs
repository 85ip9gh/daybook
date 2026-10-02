using Ledger.Api.Data;
using Ledger.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Api.Endpoints;

internal static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/accounts").WithTags("Accounts");
        group.MapPost("/", OpenAsync);
        group.MapGet("/", ListAsync);
        group.MapPost("/{id:guid}/deactivate", DeactivateAsync);
        group.MapGet("/{id:guid}/balance", BalanceAsync);
        return routes;
    }

    private static async Task<IResult> OpenAsync(OpenAccountRequest request, LedgerDbContext db, CancellationToken cancellationToken)
    {
        var account = Account.Open(request.Code, request.Name, request.Type, request.Currency);
        db.Accounts.Add(account);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return Problems.Conflict("account_code_taken", $"An account with code {account.Code} already exists.");
        }

        return Results.Created($"/accounts/{account.Id}", account.ToResponse());
    }

    private static async Task<IResult> ListAsync(LedgerDbContext db, CancellationToken cancellationToken)
    {
        var accounts = await db.Accounts.AsNoTracking().OrderBy(account => account.Code).ToListAsync(cancellationToken);
        return Results.Ok(accounts.Select(account => account.ToResponse()));
    }

    private static async Task<IResult> DeactivateAsync(Guid id, LedgerDbContext db, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(account => account.Id == id, cancellationToken);
        if (account is null)
        {
            return Problems.NotFound("account_not_found", $"Account {id} does not exist.");
        }

        account.Deactivate();
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(account.ToResponse());
    }

    private static async Task<IResult> BalanceAsync(
        Guid id,
        DateOnly? asOf,
        LedgerDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(account => account.Id == id, cancellationToken);
        if (account is null)
        {
            return Problems.NotFound("account_not_found", $"Account {id} does not exist.");
        }

        var date = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var totals = (await db.TotalsAsync(date, id, cancellationToken)).SingleOrDefault() ?? new AccountTotals(id, 0, 0);
        var normalSide = account.Type.NormalSide();

        return Results.Ok(new AccountBalanceResponse(id, date, totals.Debits, totals.Credits, totals.BalanceOn(normalSide), normalSide));
    }
}
