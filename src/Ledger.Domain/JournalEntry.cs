namespace Ledger.Domain;

public sealed class JournalEntry
{
    public const int MaxDescriptionLength = 200;
    public const int AmountScale = 4;
    public const decimal MaxAmount = 999_999_999_999_999.9999m;

    private const string ReversalPrefix = "Reversal: ";

    private readonly List<JournalLine> _lines = [];

    private JournalEntry()
    {
    }

    public Guid Id { get; private set; }

    public DateOnly EntryDate { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public string Currency { get; private set; } = string.Empty;

    public Guid? ReversesEntryId { get; private set; }

    public string PostedBy { get; private set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; private set; }

    public IReadOnlyList<JournalLine> Lines => _lines;

    public static JournalEntry Post(
        DateOnly entryDate,
        string? description,
        string? currency,
        IReadOnlyList<LineDraft> lines,
        IReadOnlyDictionary<Guid, Account> accounts,
        DateOnly? closedThrough,
        string? postedBy,
        DateTimeOffset recordedAt)
    {
        if (entryDate == default)
        {
            throw new LedgerRuleException("entry_date_required", "Entry date is required.");
        }

        AccountingPeriod.EnsureOpen(entryDate, closedThrough);

        var trimmedDescription = description?.Trim() ?? string.Empty;
        if (trimmedDescription.Length is 0 or > MaxDescriptionLength)
        {
            throw new LedgerRuleException("entry_description_invalid", $"Description must be 1 to {MaxDescriptionLength} characters.");
        }

        var entryCurrency = CurrencyCode.Parse(currency);

        if (lines.Count < 2)
        {
            throw new LedgerRuleException("entry_needs_two_lines", "A journal entry needs at least two lines.");
        }

        var debits = 0m;
        var credits = 0m;

        foreach (var line in lines)
        {
            if (!Enum.IsDefined(line.Side))
            {
                throw new LedgerRuleException("line_side_invalid", "Each line must be a debit or a credit.");
            }

            if (line.Amount <= 0)
            {
                throw new LedgerRuleException("line_amount_not_positive", "Line amounts must be greater than zero.");
            }

            if (line.Amount > MaxAmount)
            {
                throw new LedgerRuleException("line_amount_too_large", $"Line amounts cannot exceed {MaxAmount}.");
            }

            if (decimal.Round(line.Amount, AmountScale) != line.Amount)
            {
                throw new LedgerRuleException("line_amount_too_precise", $"Line amounts carry at most {AmountScale} decimal places.");
            }

            if (!accounts.TryGetValue(line.AccountId, out var account))
            {
                throw new LedgerRuleException("account_not_found", $"Account {line.AccountId} does not exist.");
            }

            if (!account.IsActive)
            {
                throw new LedgerRuleException("account_inactive", $"Account {account.Code} is inactive.");
            }

            if (account.Currency != entryCurrency)
            {
                throw new LedgerRuleException("currency_mismatch", $"Account {account.Code} is held in {account.Currency}, not {entryCurrency}.");
            }

            if (line.Side == EntrySide.Debit)
            {
                debits += line.Amount;
            }
            else
            {
                credits += line.Amount;
            }
        }

        if (debits != credits)
        {
            throw new LedgerRuleException("entry_unbalanced", $"Debits of {debits} do not equal credits of {credits}.");
        }

        var entry = new JournalEntry
        {
            Id = Guid.NewGuid(),
            EntryDate = entryDate,
            Description = trimmedDescription,
            Currency = entryCurrency,
            PostedBy = Actor.Require(postedBy),
            RecordedAt = recordedAt,
        };

        for (var index = 0; index < lines.Count; index++)
        {
            entry._lines.Add(new JournalLine(entry.Id, index + 1, lines[index].AccountId, lines[index].Side, lines[index].Amount));
        }

        return entry;
    }

    public JournalEntry Reverse(DateOnly entryDate, DateOnly? closedThrough, string? postedBy, DateTimeOffset recordedAt)
    {
        if (ReversesEntryId is not null)
        {
            throw new LedgerRuleException("reversal_not_reversible", "A reversal cannot itself be reversed.");
        }

        if (entryDate < EntryDate)
        {
            throw new LedgerRuleException("reversal_before_original", "A reversal cannot be dated before the entry it reverses.");
        }

        AccountingPeriod.EnsureOpen(entryDate, closedThrough);

        var description = ReversalPrefix + Description;
        var reversal = new JournalEntry
        {
            Id = Guid.NewGuid(),
            EntryDate = entryDate,
            Description = description.Length > MaxDescriptionLength ? description[..MaxDescriptionLength] : description,
            Currency = Currency,
            ReversesEntryId = Id,
            PostedBy = Actor.Require(postedBy),
            RecordedAt = recordedAt,
        };

        foreach (var line in _lines.OrderBy(line => line.LineNumber))
        {
            var opposite = line.Side == EntrySide.Debit ? EntrySide.Credit : EntrySide.Debit;
            reversal._lines.Add(new JournalLine(reversal.Id, line.LineNumber, line.AccountId, opposite, line.Amount));
        }

        return reversal;
    }
}
