using System.Security.Claims;
using Ledger.Api.Auth;
using Ledger.Api.Data;
using Ledger.Domain;
using Ledger.Domain.Receivables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Api.Endpoints;

internal static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/payments").WithTags("Receivables");
        group.MapPost("/", ReceiveAsync).RequireAuthorization(Policies.Record);
        group.MapGet("/{id:guid}", GetAsync).RequireAuthorization(Policies.Read);
        return routes;
    }

    private static async Task<IResult> ReceiveAsync(
        ReceivePaymentRequest request,
        [FromHeader(Name = Idempotency.KeyHeader)] string? idempotencyKey,
        LedgerDbContext db,
        TimeProvider clock,
        ClaimsPrincipal user,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var key = Idempotency.ReadKey(idempotencyKey);
        if (key is null)
        {
            return Idempotency.KeyRequired();
        }

        var hash = RequestHash.Of(request);

        var replay = await ReplayAsync(db, key, hash, http, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var closedThrough = await db.HoldPeriodsOpenAsync(cancellationToken);

        if (!await db.Customers.AnyAsync(customer => customer.Id == request.CustomerId, cancellationToken))
        {
            throw new LedgerRuleException("customer_not_found", $"Customer {request.CustomerId} does not exist.");
        }

        var invoiceIds = request.Allocations.Select(allocation => allocation.InvoiceId).Distinct().ToList();
        var invoices = await db.Invoices
            .Where(invoice => invoiceIds.Contains(invoice.Id))
            .ToDictionaryAsync(invoice => invoice.Id, cancellationToken);

        var received = Payment.Receive(
            request.CustomerId,
            request.PaymentDate,
            request.Amount,
            request.Currency,
            request.DepositAccountId,
            [.. request.Allocations.Select(allocation => new AllocationDraft(allocation.InvoiceId, allocation.Amount))],
            invoices,
            user.Name());

        var entry = JournalEntry.Post(
            request.PaymentDate,
            $"Payment from customer {request.CustomerId:N}",
            received.Payment.Currency,
            received.Posting,
            await InvoiceEndpoints.AccountsForAsync(db, received.Posting, cancellationToken),
            closedThrough,
            user.Name(),
            clock.GetUtcNow());

        received.Payment.RecordPosting(entry.Id);
        db.JournalEntries.Add(entry);
        db.Payments.Add(received.Payment);
        db.IdempotencyRecords.Add(new IdempotencyRecord { Key = key, RequestHash = hash, JournalEntryId = entry.Id });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.Conflict("invoice_changed", "An invoice on this payment was changed by another request. Retry with the same key.");
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

        return Results.Created($"/payments/{received.Payment.Id}", received.Payment.ToResponse());
    }

    private static async Task<IResult?> ReplayAsync(
        LedgerDbContext db,
        string key,
        string hash,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var record = await Idempotency.FindAsync(db, key, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var payment = record.RequestHash == hash
            ? await db.Payments
                .AsNoTracking()
                .Include(payment => payment.Allocations)
                .SingleOrDefaultAsync(payment => payment.JournalEntryId == record.JournalEntryId, cancellationToken)
            : null;

        if (payment is null)
        {
            return Idempotency.KeyReused(key);
        }

        http.Response.Headers[Idempotency.ReplayHeader] = "true";
        return Results.Ok(payment.ToResponse());
    }

    private static async Task<IResult> GetAsync(Guid id, LedgerDbContext db, CancellationToken cancellationToken)
    {
        var payment = await db.Payments
            .AsNoTracking()
            .Include(payment => payment.Allocations)
            .SingleOrDefaultAsync(payment => payment.Id == id, cancellationToken);

        return payment is null
            ? Problems.NotFound("payment_not_found", $"Payment {id} does not exist.")
            : Results.Ok(payment.ToResponse());
    }
}
