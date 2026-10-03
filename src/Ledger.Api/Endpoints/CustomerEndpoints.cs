using Ledger.Api.Data;
using Ledger.Domain.Receivables;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Api.Endpoints;

internal static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/customers").WithTags("Receivables");
        group.MapPost("/", RegisterAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}/receivables", ReceivablesAsync);
        return routes;
    }

    private static async Task<IResult> RegisterAsync(RegisterCustomerRequest request, LedgerDbContext db, CancellationToken cancellationToken)
    {
        var customer = Customer.Register(request.Code, request.Name);
        db.Customers.Add(customer);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return Problems.Conflict("customer_code_taken", $"A customer with code {customer.Code} already exists.");
        }

        return Results.Created($"/customers/{customer.Id}", customer.ToResponse());
    }

    private static async Task<IResult> ListAsync(LedgerDbContext db, CancellationToken cancellationToken)
    {
        var customers = await db.Customers.AsNoTracking().OrderBy(customer => customer.Code).ToListAsync(cancellationToken);
        return Results.Ok(customers.Select(customer => customer.ToResponse()));
    }

    private static async Task<IResult> ReceivablesAsync(Guid id, LedgerDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Customers.AnyAsync(customer => customer.Id == id, cancellationToken))
        {
            return Problems.NotFound("customer_not_found", $"Customer {id} does not exist.");
        }

        var open = await db.Invoices
            .AsNoTracking()
            .Include(invoice => invoice.Lines)
            .Where(invoice => invoice.CustomerId == id && invoice.Status == InvoiceStatus.Issued)
            .OrderBy(invoice => invoice.Number)
            .ToListAsync(cancellationToken);

        return Results.Ok(new ReceivablesResponse(id, open.Sum(invoice => invoice.Outstanding), [.. open.Select(invoice => invoice.ToResponse())]));
    }
}
