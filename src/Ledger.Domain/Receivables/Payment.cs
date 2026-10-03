namespace Ledger.Domain.Receivables;

public readonly record struct AllocationDraft(Guid InvoiceId, decimal Amount);

public sealed record ReceivedPayment(Payment Payment, IReadOnlyList<LineDraft> Posting);

public sealed class PaymentAllocation
{
    private PaymentAllocation()
    {
    }

    internal PaymentAllocation(Guid paymentId, Guid invoiceId, decimal amount)
    {
        PaymentId = paymentId;
        InvoiceId = invoiceId;
        Amount = amount;
    }

    public Guid PaymentId { get; private set; }

    public Guid InvoiceId { get; private set; }

    public decimal Amount { get; private set; }
}

public sealed class Payment
{
    private readonly List<PaymentAllocation> _allocations = [];

    private Payment()
    {
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public DateOnly PaymentDate { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public Guid DepositAccountId { get; private set; }

    public Guid JournalEntryId { get; private set; }

    public string ReceivedBy { get; private set; } = string.Empty;

    public IReadOnlyList<PaymentAllocation> Allocations => _allocations;

    public static ReceivedPayment Receive(
        Guid customerId,
        DateOnly paymentDate,
        decimal amount,
        string? currency,
        Guid depositAccountId,
        IReadOnlyList<AllocationDraft> allocations,
        IReadOnlyDictionary<Guid, Invoice> invoices,
        string? receivedBy)
    {
        if (paymentDate == default)
        {
            throw new LedgerRuleException("payment_date_required", "A payment date is required.");
        }

        if (amount <= 0 || amount > JournalEntry.MaxAmount || !Money.FitsScale(amount))
        {
            throw new LedgerRuleException("payment_amount_invalid", $"A payment amount must be positive with at most {Money.Scale} decimal places.");
        }

        var paymentCurrency = CurrencyCode.Parse(currency);

        if (allocations.Count == 0)
        {
            throw new LedgerRuleException("payment_needs_an_allocation", "A payment must be allocated to at least one invoice.");
        }

        if (allocations.Select(allocation => allocation.InvoiceId).Distinct().Count() != allocations.Count)
        {
            throw new LedgerRuleException("allocation_duplicated", "A payment can allocate to each invoice once.");
        }

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            PaymentDate = paymentDate,
            Amount = amount,
            Currency = paymentCurrency,
            DepositAccountId = depositAccountId,
            ReceivedBy = Actor.Require(receivedBy),
        };

        var credits = new List<LineDraft>();

        foreach (var allocation in allocations)
        {
            if (allocation.Amount <= 0 || !Money.FitsScale(allocation.Amount))
            {
                throw new LedgerRuleException("allocation_amount_invalid", $"An allocation must be positive with at most {Money.Scale} decimal places.");
            }

            if (!invoices.TryGetValue(allocation.InvoiceId, out var invoice))
            {
                throw new LedgerRuleException("invoice_not_found", $"Invoice {allocation.InvoiceId} does not exist.");
            }

            if (invoice.CustomerId != customerId)
            {
                throw new LedgerRuleException("invoice_of_another_customer", $"Invoice {allocation.InvoiceId} belongs to another customer.");
            }

            if (invoice.Currency != paymentCurrency)
            {
                throw new LedgerRuleException("currency_mismatch", $"Invoice {allocation.InvoiceId} is in {invoice.Currency}, not {paymentCurrency}.");
            }

            if (invoice.IssueDate is { } issued && paymentDate < issued)
            {
                throw new LedgerRuleException("payment_before_invoice", $"Invoice {invoice.Number} was issued after the payment date.");
            }

            invoice.Apply(allocation.Amount);
            payment._allocations.Add(new PaymentAllocation(payment.Id, invoice.Id, allocation.Amount));
            credits.Add(new LineDraft(invoice.ReceivableAccountId, EntrySide.Credit, allocation.Amount));
        }

        if (payment._allocations.Sum(allocation => allocation.Amount) != amount)
        {
            throw new LedgerRuleException("payment_not_fully_allocated", "Allocations must add up to the payment amount.");
        }

        var posting = new List<LineDraft> { new(depositAccountId, EntrySide.Debit, amount) };
        posting.AddRange(credits
            .GroupBy(credit => credit.AccountId)
            .Select(group => new LineDraft(group.Key, EntrySide.Credit, group.Sum(credit => credit.Amount))));

        return new ReceivedPayment(payment, posting);
    }

    public void RecordPosting(Guid journalEntryId) => JournalEntryId = journalEntryId;
}
