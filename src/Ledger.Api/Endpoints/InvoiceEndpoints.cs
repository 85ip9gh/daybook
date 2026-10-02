using Ledger.Api.Data;
using Ledger.Domain;
using Ledger.Domain.Receivables;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Api.Endpoints;

internal static class InvoiceEndpoints
{
    public static IEndpointRouteBuilder MapInvoiceEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/invoices").WithTags("Receivables");
        group.MapPost("/", DraftAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/{id:guid}/issue", IssueAsync);
        group.MapPost("/{id:guid}/void", VoidAsync);
        return routes;
    }

    private static async Task<IResult> DraftAsync(DraftInvoiceRequest request, LedgerDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Customers.AnyAsync(customer => customer.Id == request.CustomerId, cancellationToken))
        {
            throw new LedgerRuleException("customer_not_found", $"Customer {request.CustomerId} does not exist.");
        }

        var accountIds = request.Lines.Select(line => line.RevenueAccountId).Append(request.ReceivableAccountId).ToHashSet();
        if (request.Tax is not null)
        {
            accountIds.Add(request.Tax.AccountId);
        }

        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(account => accountIds.Contains(account.Id))
            .ToDictionaryAsync(account => account.Id, cancellationToken);

        var invoice = Invoice.Draft(
            request.CustomerId,
            request.Currency,
            request.ReceivableAccountId,
            [.. request.Lines.Select(line => new InvoiceLineDraft(line.Description, line.Quantity, line.UnitPrice, line.RevenueAccountId))],
            request.Tax is null ? null : new TaxDraft(request.Tax.Rate, request.Tax.AccountId),
            accounts);

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/invoices/{invoice.Id}", invoice.ToResponse());
    }

    private static async Task<IResult> GetAsync(Guid id, LedgerDbContext db, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices
            .AsNoTracking()
            .Include(invoice => invoice.Lines)
            .SingleOrDefaultAsync(invoice => invoice.Id == id, cancellationToken);

        return invoice is null ? NotFound(id) : Results.Ok(invoice.ToResponse());
    }

    private static async Task<IResult> IssueAsync(
        Guid id,
        IssueInvoiceRequest request,
        LedgerDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var closedThrough = await db.HoldPeriodsOpenAsync(cancellationToken);

        var invoice = await db.Invoices.Include(invoice => invoice.Lines).SingleOrDefaultAsync(invoice => invoice.Id == id, cancellationToken);
        if (invoice is null)
        {
            return NotFound(id);
        }

        invoice.EnsureDraft();
        AccountingPeriod.EnsureOpen(request.IssueDate, closedThrough);

        var number = await db.NextNumberAsync(NumberSequence.Invoice, cancellationToken);
        var posting = invoice.Issue(number, request.IssueDate, request.DueDate);
        var accounts = await AccountsForAsync(db, posting, cancellationToken);

        var entry = JournalEntry.Post(
            request.IssueDate,
            $"Invoice {number}",
            invoice.Currency,
            posting,
            accounts,
            closedThrough,
            clock.GetUtcNow());

        invoice.RecordPosting(entry.Id);
        db.JournalEntries.Add(entry);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Changed(id);
        }

        return Results.Ok(invoice.ToResponse());
    }

    private static async Task<IResult> VoidAsync(
        Guid id,
        VoidInvoiceRequest request,
        LedgerDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var closedThrough = await db.HoldPeriodsOpenAsync(cancellationToken);

        var invoice = await db.Invoices.Include(invoice => invoice.Lines).SingleOrDefaultAsync(invoice => invoice.Id == id, cancellationToken);
        if (invoice is null)
        {
            return NotFound(id);
        }

        invoice.Void();

        var issued = await db.JournalEntries
            .AsNoTracking()
            .Include(entry => entry.Lines)
            .SingleAsync(entry => entry.Id == invoice.JournalEntryId, cancellationToken);

        db.JournalEntries.Add(issued.Reverse(request.VoidDate, closedThrough, clock.GetUtcNow()));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Changed(id);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return Changed(id);
        }

        return Results.Ok(invoice.ToResponse());
    }

    internal static async Task<Dictionary<Guid, Account>> AccountsForAsync(
        LedgerDbContext db,
        IReadOnlyList<LineDraft> posting,
        CancellationToken cancellationToken)
    {
        var accountIds = posting.Select(line => line.AccountId).Distinct().ToList();
        return await db.Accounts
            .AsNoTracking()
            .Where(account => accountIds.Contains(account.Id))
            .ToDictionaryAsync(account => account.Id, cancellationToken);
    }

    internal static IResult Changed(Guid id) =>
        Problems.Conflict("invoice_changed", $"Invoice {id} was changed by another request. Read it again and retry.");

    private static IResult NotFound(Guid id) => Problems.NotFound("invoice_not_found", $"Invoice {id} does not exist.");
}
