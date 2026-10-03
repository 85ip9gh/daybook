using Ledger.Api.Auth;
using Ledger.Api.Data;
using Ledger.Domain;
using Ledger.Domain.Receivables;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Api.Endpoints;

public sealed record IncomeStatementResponse(DateOnly From, DateOnly To, IncomeStatement Statement, decimal NetIncome);

public sealed record BalanceSheetResponse(DateOnly AsOf, BalanceSheet Sheet, bool IsBalanced);

public sealed record AgedCustomerResponse(
    Guid CustomerId,
    string Code,
    string Name,
    decimal Current,
    decimal Days1To30,
    decimal Days31To60,
    decimal Days61To90,
    decimal Over90,
    decimal Total);

public sealed record AgedReceivablesResponse(DateOnly AsOf, IReadOnlyList<AgedCustomerResponse> Customers, decimal Total);

public sealed record AccountStatementLine(
    Guid JournalEntryId,
    int LineNumber,
    DateOnly EntryDate,
    string Description,
    EntrySide Side,
    decimal Amount,
    decimal Balance);

public sealed record AccountStatementResponse(
    Guid AccountId,
    DateOnly From,
    DateOnly To,
    decimal OpeningBalance,
    IReadOnlyList<AccountStatementLine> Lines,
    decimal ClosingBalance,
    bool Truncated);

internal static class ReportEndpoints
{
    public const int MaxStatementLines = 1000;

    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/").WithTags("Reports").RequireAuthorization(Policies.Read);
        group.MapGet("/trial-balance", TrialBalanceAsync);
        group.MapGet("/reports/income-statement", IncomeStatementAsync);
        group.MapGet("/reports/balance-sheet", BalanceSheetAsync);
        group.MapGet("/reports/aged-receivables", AgedReceivablesAsync);
        group.MapGet("/accounts/{id:guid}/statement", AccountStatementAsync);
        return routes;
    }

    private static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    private static async Task<IResult> TrialBalanceAsync(
        DateOnly? asOf,
        LedgerDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var date = asOf ?? Today(clock);
        var totals = await db.TotalsAsync(date, null, cancellationToken);
        var accounts = await db.Accounts.AsNoTracking().ToListAsync(cancellationToken);
        var trialBalance = TrialBalance.From(accounts, totals);

        return Results.Ok(new TrialBalanceResponse(date, trialBalance.Rows, trialBalance.TotalDebit, trialBalance.TotalCredit));
    }

    private static async Task<IResult> IncomeStatementAsync(DateOnly from, DateOnly to, LedgerDbContext db, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new LedgerRuleException("report_range_invalid", "A report cannot end before it starts.");
        }

        var activity = await db.ActivityAsync(from, to, null, cancellationToken);
        var accounts = await db.Accounts.AsNoTracking().ToListAsync(cancellationToken);
        var statement = IncomeStatement.From(accounts, activity);

        return Results.Ok(new IncomeStatementResponse(from, to, statement, statement.NetIncome));
    }

    private static async Task<IResult> BalanceSheetAsync(DateOnly? asOf, LedgerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var date = asOf ?? Today(clock);
        var totals = await db.TotalsAsync(date, null, cancellationToken);
        var accounts = await db.Accounts.AsNoTracking().ToListAsync(cancellationToken);
        var sheet = BalanceSheet.From(accounts, totals);

        return Results.Ok(new BalanceSheetResponse(date, sheet, sheet.IsBalanced));
    }

    private static async Task<IResult> AgedReceivablesAsync(DateOnly? asOf, LedgerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var date = asOf ?? Today(clock);

        var open = await db.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.IssueDate != null
                && invoice.IssueDate <= date
                && (invoice.VoidedOn == null || invoice.VoidedOn > date))
            .Select(invoice => new
            {
                invoice.CustomerId,
                invoice.DueDate,
                Outstanding = invoice.Total - db.PaymentAllocations
                    .Where(allocation => allocation.InvoiceId == invoice.Id
                        && db.Payments.Any(payment => payment.Id == allocation.PaymentId && payment.PaymentDate <= date))
                    .Sum(allocation => allocation.Amount),
            })
            .Where(invoice => invoice.Outstanding > 0)
            .ToListAsync(cancellationToken);

        var aged = Aging.Age(open.Select(invoice => new OpenInvoice(invoice.CustomerId, invoice.DueDate!.Value, invoice.Outstanding)), date);
        var customerIds = aged.Select(customer => customer.CustomerId).ToList();
        var customers = await db.Customers
            .AsNoTracking()
            .Where(customer => customerIds.Contains(customer.Id))
            .ToDictionaryAsync(customer => customer.Id, cancellationToken);

        var rows = aged
            .Select(row => new AgedCustomerResponse(
                row.CustomerId,
                customers[row.CustomerId].Code,
                customers[row.CustomerId].Name,
                row.Current,
                row.Days1To30,
                row.Days31To60,
                row.Days61To90,
                row.Over90,
                row.Total))
            .OrderBy(row => row.Code, StringComparer.Ordinal)
            .ToList();

        return Results.Ok(new AgedReceivablesResponse(date, rows, rows.Sum(row => row.Total)));
    }

    private static async Task<IResult> AccountStatementAsync(
        Guid id,
        DateOnly from,
        DateOnly to,
        LedgerDbContext db,
        CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(account => account.Id == id, cancellationToken);
        if (account is null)
        {
            return Problems.NotFound("account_not_found", $"Account {id} does not exist.");
        }

        if (to < from)
        {
            throw new LedgerRuleException("report_range_invalid", "A report cannot end before it starts.");
        }

        var normalSide = account.Type.NormalSide();
        var before = from == DateOnly.MinValue
            ? null
            : (await db.TotalsAsync(from.AddDays(-1), id, cancellationToken)).SingleOrDefault();
        var opening = before?.BalanceOn(normalSide) ?? 0m;

        var rows = await db.StatementAsync(id, from, to, MaxStatementLines + 1, cancellationToken);
        var lines = rows
            .Take(MaxStatementLines)
            .Select(row => new AccountStatementLine(
                row.JournalEntryId,
                row.LineNumber,
                row.EntryDate,
                row.Description,
                (EntrySide)row.Side,
                row.Amount,
                opening + new AccountTotals(id, row.RunningDebits, row.RunningCredits).BalanceOn(normalSide)))
            .ToList();

        return Results.Ok(new AccountStatementResponse(
            id,
            from,
            to,
            opening,
            lines,
            lines.Count == 0 ? opening : lines[^1].Balance,
            rows.Count > MaxStatementLines));
    }
}
