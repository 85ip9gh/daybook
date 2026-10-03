namespace Ledger.Api.Data;

public sealed class LedgerState
{
    public const byte SingletonId = 1;

    public byte Id { get; init; } = SingletonId;

    public DateOnly? ClosedThrough { get; init; }
}

public sealed class PeriodClose
{
    public required DateOnly PeriodEnd { get; init; }

    public required DateTimeOffset ClosedAt { get; init; }

    public required string ClosedBy { get; init; }
}

public sealed class PeriodBalance
{
    public required DateOnly PeriodEnd { get; init; }

    public required Guid AccountId { get; init; }

    public required decimal Debits { get; init; }

    public required decimal Credits { get; init; }
}
