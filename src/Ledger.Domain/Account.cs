namespace Ledger.Domain;

public sealed class Account
{
    public const int MaxCodeLength = 20;
    public const int MaxNameLength = 100;

    private Account()
    {
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public AccountType Type { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public static Account Open(string? code, string? name, AccountType type, string? currency)
    {
        var trimmedCode = code?.Trim() ?? string.Empty;
        var trimmedName = name?.Trim() ?? string.Empty;

        if (trimmedCode.Length is 0 or > MaxCodeLength)
        {
            throw new LedgerRuleException("account_code_invalid", $"Account code must be 1 to {MaxCodeLength} characters.");
        }

        if (trimmedName.Length is 0 or > MaxNameLength)
        {
            throw new LedgerRuleException("account_name_invalid", $"Account name must be 1 to {MaxNameLength} characters.");
        }

        if (!Enum.IsDefined(type))
        {
            throw new LedgerRuleException("account_type_invalid", "Account type is not recognised.");
        }

        return new Account
        {
            Id = Guid.NewGuid(),
            Code = trimmedCode,
            Name = trimmedName,
            Type = type,
            Currency = CurrencyCode.Parse(currency),
            IsActive = true,
        };
    }

    public void Deactivate() => IsActive = false;
}
