using Ledger.Domain.Receivables;

namespace Ledger.Api;

public sealed record RegisterCustomerRequest(string Code, string Name);

public sealed record CustomerResponse(Guid Id, string Code, string Name);

public sealed record InvoiceLineRequest(string Description, decimal Quantity, decimal UnitPrice, Guid RevenueAccountId);

public sealed record InvoiceTaxRequest(decimal Rate, Guid AccountId);

public sealed record DraftInvoiceRequest(
    Guid CustomerId,
    string Currency,
    Guid ReceivableAccountId,
    IReadOnlyList<InvoiceLineRequest> Lines,
    InvoiceTaxRequest? Tax = null);

public sealed record IssueInvoiceRequest(DateOnly IssueDate, DateOnly DueDate);

public sealed record VoidInvoiceRequest(DateOnly VoidDate);

public sealed record InvoiceLineResponse(int LineNumber, string Description, decimal Quantity, decimal UnitPrice, Guid RevenueAccountId, decimal Amount);

public sealed record InvoiceResponse(
    Guid Id,
    Guid CustomerId,
    InvoiceStatus Status,
    int? Number,
    string Currency,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    decimal Subtotal,
    decimal Tax,
    decimal Total,
    decimal AmountPaid,
    decimal Outstanding,
    Guid? JournalEntryId,
    IReadOnlyList<InvoiceLineResponse> Lines);

public sealed record AllocationRequest(Guid InvoiceId, decimal Amount);

public sealed record ReceivePaymentRequest(
    Guid CustomerId,
    DateOnly PaymentDate,
    decimal Amount,
    string Currency,
    Guid DepositAccountId,
    IReadOnlyList<AllocationRequest> Allocations);

public sealed record AllocationResponse(Guid InvoiceId, decimal Amount);

public sealed record PaymentResponse(
    Guid Id,
    Guid CustomerId,
    DateOnly PaymentDate,
    decimal Amount,
    string Currency,
    Guid DepositAccountId,
    Guid JournalEntryId,
    IReadOnlyList<AllocationResponse> Allocations);

public sealed record ReceivablesResponse(Guid CustomerId, decimal Outstanding, IReadOnlyList<InvoiceResponse> Invoices);

internal static class ReceivablesMappings
{
    public static CustomerResponse ToResponse(this Customer customer) => new(customer.Id, customer.Code, customer.Name);

    public static InvoiceResponse ToResponse(this Invoice invoice) =>
        new(
            invoice.Id,
            invoice.CustomerId,
            invoice.Status,
            invoice.Number,
            invoice.Currency,
            invoice.IssueDate,
            invoice.DueDate,
            invoice.Subtotal,
            invoice.Tax,
            invoice.Total,
            invoice.AmountPaid,
            invoice.Outstanding,
            invoice.JournalEntryId,
            [.. invoice.Lines
                .OrderBy(line => line.LineNumber)
                .Select(line => new InvoiceLineResponse(line.LineNumber, line.Description, line.Quantity, line.UnitPrice, line.RevenueAccountId, line.Amount))]);

    public static PaymentResponse ToResponse(this Payment payment) =>
        new(
            payment.Id,
            payment.CustomerId,
            payment.PaymentDate,
            payment.Amount,
            payment.Currency,
            payment.DepositAccountId,
            payment.JournalEntryId,
            [.. payment.Allocations.Select(allocation => new AllocationResponse(allocation.InvoiceId, allocation.Amount))]);
}
