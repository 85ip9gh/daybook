namespace Ledger.Api.Data;

public sealed class IdempotencyRecord
{
    public const int MaxKeyLength = 100;
    public const int HashLength = 64;

    public required string Key { get; init; }

    public required string RequestHash { get; init; }

    public required Guid JournalEntryId { get; init; }
}
