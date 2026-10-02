using Ledger.Domain;

namespace Ledger.Api;

public sealed record OpenAccountRequest(string Code, string Name, AccountType Type, string Currency);

public sealed record AccountResponse(Guid Id, string Code, string Name, AccountType Type, string Currency, bool IsActive);

public sealed record AccountBalanceResponse(Guid AccountId, DateOnly AsOf, decimal Debits, decimal Credits, decimal Balance, EntrySide NormalSide);

public sealed record PostLineRequest(Guid AccountId, EntrySide Side, decimal Amount);

public sealed record PostEntryRequest(DateOnly EntryDate, string Description, string Currency, IReadOnlyList<PostLineRequest> Lines);

public sealed record ReverseEntryRequest(DateOnly EntryDate);

public sealed record LineResponse(int LineNumber, Guid AccountId, EntrySide Side, decimal Amount);

public sealed record EntryResponse(
    Guid Id,
    DateOnly EntryDate,
    string Description,
    string Currency,
    Guid? ReversesEntryId,
    DateTimeOffset RecordedAt,
    IReadOnlyList<LineResponse> Lines);

public sealed record TrialBalanceResponse(DateOnly AsOf, IReadOnlyList<TrialBalanceRow> Rows, decimal TotalDebit, decimal TotalCredit);

public sealed record PeriodCloseResponse(DateOnly PeriodEnd, DateTimeOffset ClosedAt);

public sealed record PeriodsResponse(DateOnly? ClosedThrough, IReadOnlyList<PeriodCloseResponse> Closes);

internal static class ContractMappings
{
    public static AccountResponse ToResponse(this Account account) =>
        new(account.Id, account.Code, account.Name, account.Type, account.Currency, account.IsActive);

    public static EntryResponse ToResponse(this JournalEntry entry) =>
        new(
            entry.Id,
            entry.EntryDate,
            entry.Description,
            entry.Currency,
            entry.ReversesEntryId,
            entry.RecordedAt,
            [.. entry.Lines
                .OrderBy(line => line.LineNumber)
                .Select(line => new LineResponse(line.LineNumber, line.AccountId, line.Side, line.Amount))]);
}
