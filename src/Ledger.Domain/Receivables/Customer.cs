namespace Ledger.Domain.Receivables;

public sealed class Customer
{
    public const int MaxCodeLength = 20;
    public const int MaxNameLength = 100;

    private Customer()
    {
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public static Customer Register(string? code, string? name)
    {
        var trimmedCode = code?.Trim() ?? string.Empty;
        var trimmedName = name?.Trim() ?? string.Empty;

        if (trimmedCode.Length is 0 or > MaxCodeLength)
        {
            throw new LedgerRuleException("customer_code_invalid", $"Customer code must be 1 to {MaxCodeLength} characters.");
        }

        if (trimmedName.Length is 0 or > MaxNameLength)
        {
            throw new LedgerRuleException("customer_name_invalid", $"Customer name must be 1 to {MaxNameLength} characters.");
        }

        return new Customer { Id = Guid.NewGuid(), Code = trimmedCode, Name = trimmedName };
    }
}
