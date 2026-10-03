using System.Security.Cryptography;
using System.Text;

namespace Ledger.Domain.Banking;

public enum StatementStatus
{
    Pending = 1,
    Processing = 2,
    Processed = 3,
    Failed = 4,
}

public sealed class BankStatement
{
    public const int MaxContentLength = 1_000_000;
    public const int MaxErrorLength = 2000;
    public const int MaxAttempts = 5;
    public const int HashLength = 64;

    private BankStatement()
    {
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public string ContentHash { get; private set; } = string.Empty;

    public string Content { get; private set; } = string.Empty;

    public StatementStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? ClaimedAt { get; private set; }

    public string ImportedBy { get; private set; } = string.Empty;

    public DateTimeOffset ImportedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int LineCount { get; private set; }

    public int NewLineCount { get; private set; }

    public int MatchedCount { get; private set; }

    public string? Error { get; private set; }

    public static BankStatement Import(Account account, string? content, string? importedBy, DateTimeOffset importedAt)
    {
        if (account.Type != AccountType.Asset || !account.IsActive)
        {
            throw new LedgerRuleException("bank_account_invalid", $"Account {account.Code} is not an active asset account, so it cannot take a bank statement.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new LedgerRuleException("statement_empty", "The statement has no content.");
        }

        if (content.Length > MaxContentLength)
        {
            throw new LedgerRuleException("statement_too_large", $"A statement holds at most {MaxContentLength} characters.");
        }

        return new BankStatement
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Content = content,
            ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))),
            Status = StatementStatus.Pending,
            ImportedBy = Actor.Require(importedBy),
            ImportedAt = importedAt,
        };
    }

    public void Complete(int lineCount, int newLineCount, int matchedCount, DateTimeOffset processedAt)
    {
        Status = StatementStatus.Processed;
        LineCount = lineCount;
        NewLineCount = newLineCount;
        MatchedCount = matchedCount;
        ProcessedAt = processedAt;
        Error = null;
    }

    public void Fail(string error, DateTimeOffset processedAt)
    {
        Status = StatementStatus.Failed;
        ProcessedAt = processedAt;
        Error = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
    }

    public void Release(DateTimeOffset at)
    {
        if (Attempts >= MaxAttempts)
        {
            Fail($"Gave up after {Attempts} attempts that each lost a race with another statement.", at);
            return;
        }

        Status = StatementStatus.Pending;
        ClaimedAt = null;
    }
}

public sealed class BankLine
{
    private BankLine()
    {
    }

    public Guid Id { get; private set; }

    public Guid StatementId { get; private set; }

    public Guid AccountId { get; private set; }

    public string Reference { get; private set; } = string.Empty;

    public DateOnly Date { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }

    public Guid? MatchedEntryId { get; private set; }

    public int? MatchedLineNumber { get; private set; }

    public string? MatchedBy { get; private set; }

    public DateTimeOffset? MatchedAt { get; private set; }

    public bool IsMatched => MatchedEntryId is not null;

    public static BankLine From(BankStatement statement, StatementLineDraft draft) =>
        new()
        {
            Id = Guid.NewGuid(),
            StatementId = statement.Id,
            AccountId = statement.AccountId,
            Reference = draft.Reference,
            Date = draft.Date,
            Description = draft.Description,
            Amount = draft.Amount,
        };

    public static decimal Signed(EntrySide side, decimal amount) => side == EntrySide.Debit ? amount : -amount;

    public void MatchTo(Guid journalEntryId, int lineNumber, decimal ledgerSignedAmount, string? matchedBy, DateTimeOffset matchedAt)
    {
        if (IsMatched)
        {
            throw new LedgerRuleException("bank_line_already_matched", $"Bank line {Reference} is already matched.");
        }

        if (ledgerSignedAmount != Amount)
        {
            throw new LedgerRuleException("match_amount_differs", $"Bank line {Reference} is {Amount} and the ledger line is {ledgerSignedAmount}.");
        }

        MatchedEntryId = journalEntryId;
        MatchedLineNumber = lineNumber;
        MatchedBy = Actor.Require(matchedBy);
        MatchedAt = matchedAt;
    }

    public void Unmatch()
    {
        if (!IsMatched)
        {
            throw new LedgerRuleException("bank_line_not_matched", $"Bank line {Reference} is not matched.");
        }

        MatchedEntryId = null;
        MatchedLineNumber = null;
        MatchedBy = null;
        MatchedAt = null;
    }
}
