namespace Ledger.Domain.Receivables;

public enum InvoiceStatus
{
    Draft = 1,
    Issued = 2,
    Paid = 3,
    Void = 4,
}

public readonly record struct InvoiceLineDraft(string? Description, decimal Quantity, decimal UnitPrice, Guid RevenueAccountId);

public readonly record struct TaxDraft(decimal Rate, Guid AccountId);

public sealed class InvoiceLine
{
    public const int MaxDescriptionLength = 200;

    private InvoiceLine()
    {
    }

    internal InvoiceLine(Guid invoiceId, int lineNumber, string description, decimal quantity, decimal unitPrice, Guid revenueAccountId, decimal amount)
    {
        InvoiceId = invoiceId;
        LineNumber = lineNumber;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        RevenueAccountId = revenueAccountId;
        Amount = amount;
    }

    public Guid InvoiceId { get; private set; }

    public int LineNumber { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public decimal Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public Guid RevenueAccountId { get; private set; }

    public decimal Amount { get; private set; }
}

public sealed class Invoice
{
    public const int QuantityScale = 4;

    private readonly List<InvoiceLine> _lines = [];

    private Invoice()
    {
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public Guid ReceivableAccountId { get; private set; }

    public Guid? TaxAccountId { get; private set; }

    public decimal TaxRate { get; private set; }

    public decimal Subtotal { get; private set; }

    public decimal Tax { get; private set; }

    public decimal Total { get; private set; }

    public decimal AmountPaid { get; private set; }

    public InvoiceStatus Status { get; private set; }

    public int? Number { get; private set; }

    public DateOnly? IssueDate { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public Guid? JournalEntryId { get; private set; }

    public IReadOnlyList<InvoiceLine> Lines => _lines;

    public decimal Outstanding => Status == InvoiceStatus.Issued ? Total - AmountPaid : 0m;

    public static Invoice Draft(
        Guid customerId,
        string? currency,
        Guid receivableAccountId,
        IReadOnlyList<InvoiceLineDraft> lines,
        TaxDraft? tax,
        IReadOnlyDictionary<Guid, Account> accounts)
    {
        var invoiceCurrency = CurrencyCode.Parse(currency);

        RequireAccount(accounts, receivableAccountId, AccountType.Asset, invoiceCurrency, "receivable_account_invalid", "receivable");

        if (lines.Count == 0)
        {
            throw new LedgerRuleException("invoice_needs_a_line", "An invoice needs at least one line.");
        }

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Currency = invoiceCurrency,
            ReceivableAccountId = receivableAccountId,
            Status = InvoiceStatus.Draft,
        };

        foreach (var line in lines)
        {
            var description = line.Description?.Trim() ?? string.Empty;
            if (description.Length is 0 or > InvoiceLine.MaxDescriptionLength)
            {
                throw new LedgerRuleException("invoice_line_description_invalid", $"Line descriptions must be 1 to {InvoiceLine.MaxDescriptionLength} characters.");
            }

            if (line.Quantity <= 0 || decimal.Round(line.Quantity, QuantityScale) != line.Quantity)
            {
                throw new LedgerRuleException("invoice_line_quantity_invalid", $"Quantities must be positive with at most {QuantityScale} decimal places.");
            }

            if (line.UnitPrice <= 0 || line.UnitPrice > JournalEntry.MaxAmount || decimal.Round(line.UnitPrice, JournalEntry.AmountScale) != line.UnitPrice)
            {
                throw new LedgerRuleException("invoice_line_price_invalid", $"Unit prices must be positive with at most {JournalEntry.AmountScale} decimal places.");
            }

            var amount = Money.Round(line.Quantity * line.UnitPrice);
            if (amount <= 0)
            {
                throw new LedgerRuleException("invoice_line_amount_zero", "A line cannot round to zero.");
            }

            RequireAccount(accounts, line.RevenueAccountId, AccountType.Revenue, invoiceCurrency, "revenue_account_invalid", "revenue");

            invoice._lines.Add(new InvoiceLine(invoice.Id, invoice._lines.Count + 1, description, line.Quantity, line.UnitPrice, line.RevenueAccountId, amount));
        }

        invoice.Subtotal = invoice._lines.Sum(line => line.Amount);

        if (tax is { } taxDraft)
        {
            if (taxDraft.Rate is <= 0 or >= 1)
            {
                throw new LedgerRuleException("tax_rate_invalid", "A tax rate is a fraction between 0 and 1.");
            }

            RequireAccount(accounts, taxDraft.AccountId, AccountType.Liability, invoiceCurrency, "tax_account_invalid", "tax");

            invoice.TaxRate = taxDraft.Rate;
            invoice.TaxAccountId = taxDraft.AccountId;
            invoice.Tax = Money.Round(invoice.Subtotal * taxDraft.Rate);
        }

        invoice.Total = invoice.Subtotal + invoice.Tax;
        if (invoice.Total > JournalEntry.MaxAmount)
        {
            throw new LedgerRuleException("invoice_total_too_large", $"An invoice total cannot exceed {JournalEntry.MaxAmount}.");
        }

        return invoice;
    }

    public void EnsureDraft()
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw new LedgerRuleException("invoice_not_draft", "Only a draft invoice can be issued.");
        }
    }

    public IReadOnlyList<LineDraft> Issue(int number, DateOnly issueDate, DateOnly dueDate)
    {
        EnsureDraft();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);

        if (issueDate == default)
        {
            throw new LedgerRuleException("invoice_issue_date_required", "An issue date is required.");
        }

        if (dueDate < issueDate)
        {
            throw new LedgerRuleException("invoice_due_before_issue", "An invoice cannot fall due before it is issued.");
        }

        Status = InvoiceStatus.Issued;
        Number = number;
        IssueDate = issueDate;
        DueDate = dueDate;

        var posting = new List<LineDraft> { new(ReceivableAccountId, EntrySide.Debit, Total) };
        posting.AddRange(_lines
            .GroupBy(line => line.RevenueAccountId)
            .Select(group => new LineDraft(group.Key, EntrySide.Credit, group.Sum(line => line.Amount))));

        if (TaxAccountId is { } taxAccountId && Tax > 0)
        {
            posting.Add(new LineDraft(taxAccountId, EntrySide.Credit, Tax));
        }

        return posting;
    }

    public void RecordPosting(Guid journalEntryId) => JournalEntryId = journalEntryId;

    public void Void()
    {
        if (Status != InvoiceStatus.Issued || AmountPaid != 0)
        {
            throw new LedgerRuleException("invoice_not_voidable", "Only an issued invoice with nothing paid against it can be voided.");
        }

        Status = InvoiceStatus.Void;
    }

    internal void Apply(decimal amount)
    {
        if (Status != InvoiceStatus.Issued)
        {
            throw new LedgerRuleException("invoice_not_open", $"Invoice {Id} is not open for payment.");
        }

        if (amount > Outstanding)
        {
            throw new LedgerRuleException("allocation_exceeds_outstanding", $"Invoice {Number} has {Outstanding} outstanding, less than the {amount} allocated.");
        }

        AmountPaid += amount;
        if (AmountPaid == Total)
        {
            Status = InvoiceStatus.Paid;
        }
    }

    private static void RequireAccount(
        IReadOnlyDictionary<Guid, Account> accounts,
        Guid accountId,
        AccountType type,
        string currency,
        string code,
        string role)
    {
        if (!accounts.TryGetValue(accountId, out var account) || account.Type != type || !account.IsActive || account.Currency != currency)
        {
            throw new LedgerRuleException(code, $"The {role} account must be an active {type} account held in {currency}.");
        }
    }
}
