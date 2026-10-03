namespace Ledger.Domain;

public readonly record struct LineDraft(Guid AccountId, EntrySide Side, decimal Amount);

public sealed class JournalLine
{
    private JournalLine()
    {
    }

    internal JournalLine(Guid journalEntryId, DateOnly entryDate, int lineNumber, Guid accountId, EntrySide side, decimal amount)
    {
        JournalEntryId = journalEntryId;
        EntryDate = entryDate;
        LineNumber = lineNumber;
        AccountId = accountId;
        Side = side;
        Amount = amount;
    }

    public Guid JournalEntryId { get; private set; }

    public DateOnly EntryDate { get; private set; }

    public int LineNumber { get; private set; }

    public Guid AccountId { get; private set; }

    public EntrySide Side { get; private set; }

    public decimal Amount { get; private set; }
}
