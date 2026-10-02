namespace Ledger.Domain;

public sealed class LedgerRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
